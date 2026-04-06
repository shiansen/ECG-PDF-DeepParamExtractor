import os
import numpy as np
import neurokit2 as nk
from scipy.signal import butter, filtfilt
from sklearn.metrics import *
from collections import defaultdict
from glob import glob
from tqdm import tqdm
from ecg_dataset import parse_ecg_file

FS = 500  # Hz

LABEL_KEYS = ["Hr","Pr","Qrs","Qt","Qtc","Paxis","Raxis","Taxis"]

# =========================
# 1. Preprocessing
# =========================
def bandpass_filter(signal, low=0.5, high=40, fs=500, order=3):
    nyq = 0.5 * fs
    b, a = butter(order, [low/nyq, high/nyq], btype='band')
    return filtfilt(b, a, signal)


def preprocess(ecg):
    ecg = bandpass_filter(ecg)
    ecg = nk.signal_detrend(ecg)
    ecg = nk.standardize(ecg)
    return ecg


# =========================
# 2. R peak detection (ensemble)
# =========================
def detect_r_peaks(signal):

    # Method 1: NeuroKit
    try:
        _, rpeaks_nk = nk.ecg_peaks(signal, sampling_rate=FS)
        r1 = rpeaks_nk["ECG_R_Peaks"]
    except:
        r1 = np.array([])

    return r1


# =========================
# 3. Delineation
# =========================

# ======================================
# Quality check for NeuroKit delineation
# ======================================
def check_delineation_quality(waves, min_beats=3):

    required_keys = [
        "ECG_R_Peaks",
        "ECG_QRS_Onsets",
        "ECG_QRS_Offsets"
    ]

    for k in required_keys:
        if k not in waves:
            return False

    r = waves["ECG_R_Peaks"]
    q_on = waves["ECG_QRS_Onsets"]
    q_off = waves["ECG_QRS_Offsets"]

    if len(r) < min_beats:
        return False

    # 有效 QRS 數量
    valid = np.sum(~np.isnan(q_on) & ~np.isnan(q_off))

    if valid < min_beats:
        return False

    return True


# ======================================
# Fallback delineation（強化版）
# ======================================
def fallback_delineate(signal, rpeaks):

    beats = []

    for r in rpeaks:

        left = int(r - 0.4 * FS)
        right = int(r + 0.6 * FS)

        if left < 0 or right >= len(signal):
            continue

        segment = signal[left:right]

        grad = np.gradient(segment)
        abs_grad = np.abs(grad)

        # QRS detection（限制區間）
        center = int(0.4 * FS)

        qrs_on = np.argmax(abs_grad[:center])
        qrs_off = center + np.argmax(abs_grad[center:])

        # 避免極端錯誤
        if qrs_off <= qrs_on:
            continue

        # -------------------
        # P wave（更穩定版）
        # -------------------
        p_on = None
        p_start = max(0, qrs_on - int(0.2 * FS))
        p_end = qrs_on

        if p_end - p_start > 20:
            p_seg = segment[p_start:p_end]

            # 用能量而不是單點
            energy = np.convolve(p_seg**2, np.ones(10), mode='same')
            p_idx = np.argmax(energy)

            p_on = left + p_start + p_idx

        # -------------------
        # T wave（更穩定版）
        # -------------------
        t_off = None
        t_start = qrs_off
        t_end = min(len(segment), qrs_off + int(0.4 * FS))

        if t_end - t_start > 20:
            t_seg = segment[t_start:t_end]

            energy = np.convolve(t_seg**2, np.ones(20), mode='same')
            t_idx = np.argmax(energy)

            t_off = left + t_start + t_idx

        beats.append({
            "R": r,
            "QRS_on": left + qrs_on,
            "QRS_off": left + qrs_off,
            "P_on": p_on,
            "T_off": t_off
        })

    return beats


# ======================================
# 主函式
# ======================================
def delineate_beats(signal, rpeaks):

    # ---------------------------
    # 1. NeuroKit delineation
    # ---------------------------
    try:
        signals, waves = nk.ecg_delineate(
            signal,
            rpeaks,
            sampling_rate=FS,
            method="dwt"
        )

        if check_delineation_quality(waves):

            beats = []

            r = waves["ECG_R_Peaks"]
            q_on = waves["ECG_QRS_Onsets"]
            q_off = waves["ECG_QRS_Offsets"]
            p_on = waves.get("ECG_P_Onsets", None)
            t_off = waves.get("ECG_T_Offsets", None)

            for i in range(len(r)):

                beats.append({
                    "R": r[i],
                    "QRS_on": q_on[i] if i < len(q_on) else np.nan,
                    "QRS_off": q_off[i] if i < len(q_off) else np.nan,
                    "P_on": p_on[i] if (p_on is not None and i < len(p_on)) else np.nan,
                    "T_off": t_off[i] if (t_off is not None and i < len(t_off)) else np.nan
                })

            return beats

    except Exception:
        pass

    # ---------------------------
    # 2. fallback（關鍵）
    # ---------------------------
    return fallback_delineate(signal, rpeaks)

def delineate_beats_custom(signal, rpeaks):

    beats = []

    for r in rpeaks:

        # window around R
        left = int(r - 0.4 * FS)
        right = int(r + 0.6 * FS)

        if left < 0 or right >= len(signal):
            continue

        segment = signal[left:right]

        # QRS onset/offset
        grad = np.gradient(segment)
        abs_grad = np.abs(grad)

        qrs_on = np.argmax(abs_grad[:int(0.4*FS)])
        qrs_off = int(0.4*FS) + np.argmax(abs_grad[int(0.4*FS):])

        # P wave (前 200 ms)
        p_region = segment[qrs_on-150:qrs_on] if qrs_on > 150 else None
        p_on = None

        if p_region is not None and len(p_region) > 20:
            p_on = left + (qrs_on - len(p_region)) + np.argmax(np.abs(p_region))

        # T wave (後 400 ms)
        t_region = segment[qrs_off:qrs_off+200] if qrs_off+200 < len(segment) else None
        t_off = None

        if t_region is not None and len(t_region) > 20:
            t_off = left + qrs_off + np.argmax(np.abs(t_region))

        beats.append({
            "R": r,
            "QRS_on": left + qrs_on,
            "QRS_off": left + qrs_off,
            "P_on": p_on,
            "T_off": t_off
        })

    return beats


# =========================
# 4. Feature extraction
# =========================
def compute_features(beats):

    if len(beats) < 3:
        return None

    RR = np.diff([b["R"] for b in beats]) / FS
    ventricular_rate = 60 / np.mean(RR)

    pr_list = []
    qrs_list = []
    qt_list = []

    for b in beats:
        if b["P_on"] is not None:
            pr = (b["QRS_on"] - b["P_on"]) / FS
            pr_list.append(pr)

        qrs = (b["QRS_off"] - b["QRS_on"]) / FS
        qrs_list.append(qrs)

        if b["T_off"] is not None:
            qt = (b["T_off"] - b["QRS_on"]) / FS
            qt_list.append(qt)

    def safe_mean(x):
        return np.nan if len(x) == 0 else np.mean(x)

    PR = safe_mean(pr_list)
    QRS = safe_mean(qrs_list)
    QT = safe_mean(qt_list)

    QTc = QT / np.sqrt(np.mean(RR)) if QT is not np.nan else np.nan

    return {
        "HR": ventricular_rate,
        "PR": PR,
        "QRS": QRS,
        "QT": QT,
        "QTc": QTc
    }


# =========================
# 5. Axis calculation (multi-lead)
# =========================
def compute_axis(leads):

    I = leads["I"]
    aVF = leads["aVF"]

    r_I = np.max(I) - np.min(I)
    r_aVF = np.max(aVF) - np.min(aVF)

    axis = np.degrees(np.arctan2(r_aVF, r_I))

    return axis


def extract_wave_segments(ecg, beats, wave_type):

    segments = []

    for b in beats:

        if wave_type == "P" and b["P_on"] is not None:
            start = b["P_on"]
            end = b["QRS_on"]

        elif wave_type == "QRS":
            start = b["QRS_on"]
            end = b["QRS_off"]

        elif wave_type == "T" and b["T_off"] is not None:
            start = b["QRS_off"]
            end = b["T_off"]

        else:
            continue

        if start is None or end is None:
            continue

        if end > start:
            segments.append(ecg[start:end])

    return segments

def compute_wave_axis(leads, beats, wave_type):

    lead_I = leads["I"]
    lead_aVF = leads["aVF"]
    #lead_I = preprocess(lead_I)
    #lead_aVF = preprocess(lead_aVF)

    seg_I = extract_wave_segments(lead_I, beats, wave_type)
    seg_aVF = extract_wave_segments(lead_aVF, beats, wave_type)

    if len(seg_I) == 0 or len(seg_aVF) == 0:
        return np.nan  # 👉 P axis 常見會 NaN（AF）

    amp_I = np.mean([np.sum(s) for s in seg_I])
    amp_aVF = np.mean([np.sum(s) for s in seg_aVF])

    axis = np.degrees(np.arctan2(amp_aVF, amp_I))

    return axis

def compute_all_axes(leads, beats):

    P_axis = compute_wave_axis(leads, beats, "P")
    R_axis = compute_wave_axis(leads, beats, "QRS")
    T_axis = compute_wave_axis(leads, beats, "T")

    return {
        "P_axis": P_axis,
        "R_axis": R_axis,
        "T_axis": T_axis
    }



# =========================
# 6. Metrics
# =========================
def evaluate(y_true, y_pred):
    PARAM_NAMES = [
        "ventricular_rate",
        "PR_interval",
        "QRS_duration",
        "QT",
        "QTc",
        "P_axis",
        "R_axis",
        "T_axis"
    ]

    y_true = np.array(y_true)   # (B, 8)
    y_pred = np.array(y_pred)   # (B, 8)

    assert y_true.shape == y_pred.shape
    assert y_true.shape[1] == len(PARAM_NAMES)

    results = {}

    # =========================================
    # 逐參數評估
    # =========================================
    for i, name in enumerate(PARAM_NAMES):

        yt = y_true[:, i]
        yp = y_pred[:, i]

        param_result = {}

        # -------------------------
        # 1. Regression metrics
        # -------------------------
        mask_num_yt = ~np.isnan(yt)
        mask_num_yp = ~np.isnan(yp)
        mask_num = mask_num_yt & mask_num_yp

        if np.sum(mask_num) > 0:
            yt_num = yt[mask_num]
            yp_num = yp[mask_num]

            param_result["MAE"] = mean_absolute_error(yt_num, yp_num)
            param_result["RMSE"] = np.sqrt(mean_squared_error(yt_num, yp_num))

            # R2 needs at least 2 samples
            if len(yt_num) > 1:
                param_result["R2"] = r2_score(yt_num, yp_num)
            else:
                param_result["R2"] = np.nan

        else:
            param_result["MAE"] = np.nan
            param_result["RMSE"] = np.nan
            param_result["R2"] = np.nan

        # -------------------------
        # 2. NaN classification metrics
        # -------------------------
        yt_nan = np.isnan(yt).astype(int)
        yp_nan = np.isnan(yp).astype(int)

        # 只有當 y_true 有 NaN 才有意義
        if np.unique(yt_nan).size > 1:

            param_result["accuracy"] = accuracy_score(yt_nan, yp_nan)

            param_result["precision"] = precision_score(
                yt_nan, yp_nan, zero_division=0
            )
            param_result["recall"] = recall_score(
                yt_nan, yp_nan, zero_division=0
            )
            param_result["f1"] = f1_score(
                yt_nan, yp_nan, zero_division=0
            )

            # AUROC / AUPRC（需要 probabilistic，但這裡用 binary）
            try:
                param_result["AUROC"] = roc_auc_score(yt_nan, yp_nan)
            except:
                param_result["AUROC"] = np.nan

            try:
                param_result["AUPRC"] = average_precision_score(yt_nan, yp_nan)
            except:
                param_result["AUPRC"] = np.nan

        else:
            # 沒有 NaN → classification 無意義
            param_result["accuracy"] = np.nan
            param_result["precision"] = np.nan
            param_result["recall"] = np.nan
            param_result["f1"] = np.nan
            param_result["AUROC"] = np.nan
            param_result["AUPRC"] = np.nan


        results[name] = param_result

    return results

def run_rule_based_pipeline(data_dir, fs):

    fail_counter = defaultdict(int)
    total_files = 0
    success_files = 0

    file_list = []

    # -------- 收集所有 txt --------
    for root, _, files in os.walk(data_dir):
        for f in files:
            if f.endswith(".txt"):
                file_list.append(os.path.join(root, f))

    print(f"Total files: {len(file_list)}")

    all_true = []
    all_pred = []

    pbar = tqdm(file_list, desc="Extract Parameters", leave=False)

    # -------- 主迴圈 --------
    for filepath in pbar:
        total_files += 1

        try:
            ecg, labels = parse_ecg_file(filepath)

            # -------- 選擇 lead --------
            signal = ecg.get("longII", None)
            #signal = preprocess(signal)

            if signal is None or len(signal) < fs:
                fail_counter["no_signal"] += 1
                continue

            # -------- GT --------
            gt_value = np.array([
                labels[k] for k in LABEL_KEYS
            ], dtype=np.float32)

            # -------- R peak --------
            r_peaks = detect_r_peaks(signal)

            # -------- delineation --------
            beats = delineate_beats(signal, r_peaks)

            # -------- compute --------
            results = compute_features(beats)  # HR, PR, QRS, QT, QTc
            axes = compute_all_axes(ecg, beats)  # P_axis, R_axis, T_axis

            pred = list(results.values()) + list(axes.values()) # "Hr", "Pr", "Qrs", "Qt", "Qtc", "Paxis", "Raxis", "Taxis"

            all_pred.append(pred)
            all_true.append(gt_value)

            success_files += 1

        except Exception as e:
            fail_counter["parse_error"] += 1
            continue

    results = evaluate(all_true, all_pred)

    for k, v in results.items():
        print(f"{k}: {v}")

if __name__ == "__main__":
    run_rule_based_pipeline('../ecg_data_test', FS)