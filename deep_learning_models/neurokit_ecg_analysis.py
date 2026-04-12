import os
import math
import numpy as np
import pandas as pd
from glob import glob
from tqdm import tqdm
import neurokit2 as nk
from ecg_dataset import parse_ecg_file
from collections import defaultdict
from sklearn.metrics import *
from enum import Enum

LABEL_KEYS = ["Hr","Pr","Qrs","Qt","Qtc","Paxis","Raxis","Taxis"]

class Status(Enum):
    Success = 1
    DelineateError = 2
    RPeakError = 3
    TypeError = 4

def calculate_all_axes(data_12L_dict, sampling_rate=500):
    """
    計算 P, R, T 三個軸向
    data_12L_dict: 需包含 'I' (0-2.5s) 與 'aVF' (2.5-5s) 的 cleaned 訊號
    """
    axes = {}

    def get_wave_amplitudes(signal, wave_type='R'):
        # 根據波形類型選擇特徵提取方式
        if wave_type == 'R':
            # R軸通常看整個 QRS 的淨振幅 (Max + Min，Min通常是負的S波)
            return np.max(signal) + np.min(signal)
        elif wave_type == 'P':
            # P軸通常較小，取訊號中段排除大波後的局部極大值較準確
            # 專業做法是取 P-peak 的振幅
            peaks, _ = nk.ecg_peaks(signal, sampling_rate=sampling_rate)
            # 這裡簡化為取訊號中 90th percentile 作為 P 波強度估計
            return np.percentile(signal, 95)
        elif wave_type == 'T':
            # T軸取 T-peak 振幅
            return np.max(signal)
        return 0

    # 取得 Lead I 與 aVF 的特徵
    # 注意：在 3x4+1 格式中，這兩個導程的時間段是分開的，
    # 假設傳入的 data_12L_dict['I'] 已經是那 1238 點的資料

    for wave in ['P', 'R', 'T']:
        val_i = get_wave_amplitudes(data_12L_dict['I'], wave_type=wave)
        val_avf = get_wave_amplitudes(data_12L_dict['aVF'], wave_type=wave)

        # 使用 atan2(Y, X) 計算角度
        angle = np.degrees(np.arctan2(val_avf, val_i))
        axes[f'{wave}_axis'] = angle

    return axes


def robust_get_peaks(signal, sampling_rate):
    """
    強健型 R 波偵測：嘗試多種演算法確保不回傳空值
    """
    # 嘗試方法清單
    methods = ["neurokit", "pantompkins1985", "nabian2018"]

    for method in methods:
        try:
            _, info = nk.ecg_peaks(signal, sampling_rate=sampling_rate, method=method)
            r_peaks = info["ECG_R_Peaks"]
            if len(r_peaks) >= 2:  # 至少需要兩個點才能計算心率
                return r_peaks, method
        except Exception:
            continue

    return np.array([]), None

def calculate_ecg_metrics(ecg, sampling_rate=500):
    results = {}

    data_long_ii = ecg['longII']

    # 預處理
    cleaned_long_ii = nk.ecg_clean(data_long_ii, sampling_rate=sampling_rate, method="neurokit")

    # 偵測 R 波並取得索引
    r_peaks, method = robust_get_peaks(cleaned_long_ii, sampling_rate=sampling_rate)

    # 3. 條件分支處理
    if r_peaks.size == 0:
        #print("Error: 無法偵測到任何 R 波，訊號可能無效或品質過差。")
        return results, Status.RPeakError  # 直接 empty 的結果，避免後續崩潰

    peaks_dict = {"ECG_R_Peaks": r_peaks}

    # 強制確保為整數陣列，避免與字串拼接時出錯
    r_peaks = np.array(r_peaks, dtype=int)

    # 計算心率
    hr_df = nk.ecg_rate(peaks_dict, sampling_rate=sampling_rate, desired_length=len(cleaned_long_ii))  # could be a list containing nan
    results['Ventricular_Rate'] = np.nanmean(hr_df)  # RuntimeWarning: Mean of empty slice if hr_df contains nan
    if np.isnan(results['Ventricular_Rate']):
        print('hr_df:', hr_df)
        print('R peaks:', r_peaks)
        return results, Status.RPeakError

    try:
        # 使用修正後的 r_peaks 進行區間定位
        _, waves_peak = nk.ecg_delineate(cleaned_long_ii, r_peaks,
                                         sampling_rate=sampling_rate,
                                         method="dwt")

        # 提取特徵點 (注意：Neurokit 的回傳鍵名可能因版本微調，建議先 print(waves_peak.keys()) 檢查)
        def get_mean_ms(key_start, key_end):
            start = np.array(waves_peak.get(key_start, [np.nan]))
            end = np.array(waves_peak.get(key_end, [np.nan]))
            # 濾除 nan 並計算差值
            diff = end - start
            if all(math.isnan(x) for x in diff):
                return np.nan
            return np.nanmean(diff) * (1000 / sampling_rate)

        results['PR_interval'] = get_mean_ms('ECG_P_Onsets', 'ECG_R_Onsets')
        if np.isnan(results['PR_interval']):
            return results, Status.DelineateError
        results['QRS_duration'] = get_mean_ms('ECG_R_Onsets', 'ECG_R_Offsets')
        if np.isnan(results['QRS_duration']):
            return results, Status.DelineateError
        qt_raw = get_mean_ms('ECG_R_Onsets', 'ECG_T_Offsets')
        results['QT'] = qt_raw
        if np.isnan(results['QT']):
            return results, Status.DelineateError

        # QTc 計算, Bazett
        rr_interval = 60 / results['Ventricular_Rate']
        results['QTc'] = qt_raw / np.sqrt(rr_interval)

    except TypeError as te:
        print(f"類型錯誤 (可能是 NK2 內部字串拼接問題): {te}")
        return results, Status.TypeError
    except Exception as e:
        print(f"其他 Delineation 錯誤: {e}")
        return results, Status.DelineateError

    # --- 3. 電軸計算 (Axis Calculation) ---
    axes = calculate_all_axes(ecg)
    results = {**results, **axes}

    return results, Status.Success

def bootstrap_ci(y_true, y_pred, metric_fn, n_boot=1000, alpha=0.05):

    n = len(y_true)
    stats = []

    for _ in range(n_boot):
        idx = np.random.randint(0, n, n)

        yt = y_true[idx]
        yp = y_pred[idx]

        try:
            stat = metric_fn(yt, yp)
            if not np.isnan(stat):
                stats.append(stat)
        except:
            continue

    if len(stats) < 10:
        return (np.nan, np.nan)

    lower = np.percentile(stats, 100 * (alpha/2))
    upper = np.percentile(stats, 100 * (1 - alpha/2))

    return (lower, upper)

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
            '''
            param_result["MAE"] = mean_absolute_error(yt_num, yp_num)
            param_result["RMSE"] = np.sqrt(mean_squared_error(yt_num, yp_num))

            # R2 needs at least 2 samples
            if len(yt_num) > 1:
                param_result["R2"] = r2_score(yt_num, yp_num)
            else:
                param_result["R2"] = np.nan
            '''
            # -------------------------
            # Regression metrics
            # -------------------------
            mae = mean_absolute_error(yt_num, yp_num)
            rmse = np.sqrt(mean_squared_error(yt_num, yp_num))

            if len(yt_num) > 1:
                r2 = r2_score(yt_num, yp_num)
            else:
                r2 = np.nan

            param_result["MAE"] = mae
            param_result["RMSE"] = rmse
            param_result["R2"] = r2

            # -------------------------
            # 🔥 Bootstrap CI（關鍵）
            # -------------------------
            mae_ci = bootstrap_ci(
                yt_num, yp_num,
                lambda a, b: mean_absolute_error(a, b)
            )

            rmse_ci = bootstrap_ci(
                yt_num, yp_num,
                lambda a, b: np.sqrt(mean_squared_error(a, b))
            )

            def safe_r2(a, b):
                if len(a) > 1:
                    return r2_score(a, b)
                else:
                    return np.nan

            r2_ci = bootstrap_ci(yt_num, yp_num, safe_r2)

            param_result["MAE_CI95"] = mae_ci
            param_result["RMSE_CI95"] = rmse_ci
            param_result["R2_CI95"] = r2_ci

            param_result["N"] = len(yt_num)
            param_result["N_total"] = len(yt)
            param_result["N_nan_dropped"] = len(yt) - len(yt_num)

        else:
            param_result["MAE"] = np.nan
            param_result["RMSE"] = np.nan
            param_result["R2"] = np.nan
            param_result["MAE_CI95"] = np.nan
            param_result["RMSE_CI95"] = np.nan
            param_result["R2_CI95"] = np.nan
            param_result["N"] = np.nan
            param_result["N_total"] = np.nan
            param_result["N_nan_dropped"] = np.nan

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

def four_tile(lead):
    arr = np.array(lead)
    target_len = 1250
    pad_len = max(0, target_len - len(arr))

    arr_padded = np.pad(arr, (0, pad_len), mode='constant', constant_values=0.0)
    arr_4x = np.tile(arr_padded, 4)
    return arr_4x

def run_rule_based_pipeline(data_dir):

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
            if np.isnan(labels['Pr']) or np.isnan(labels['Paxis']):
                print(F'Skipping {filepath} for nan in PR or P_axis')
                fail_counter['pr_paxis_nan'] += 1
                continue

            # -------- GT --------
            gt_value = np.array([
                labels[k] for k in LABEL_KEYS
            ], dtype=np.float32)

            final_report, status = calculate_ecg_metrics(ecg)
            if status != Status.Success:
                fail_counter['any_error'] += 1
                if status == Status.DelineateError:
                    fail_counter['delineate_error'] += 1
                elif status == Status.RPeakError:
                    fail_counter['r_peak_error'] += 1
                elif status == Status.TypeError:
                    fail_counter['type_error'] += 1
                else:
                    fail_counter['unknown_error'] += 1

                continue  # skip into next file


            pred = [final_report['Ventricular_Rate'], final_report['PR_interval'], final_report['QRS_duration'],
                    final_report['QT'], final_report['QTc'],
                    final_report['P_axis'], final_report['R_axis'], final_report['T_axis']]

            all_pred.append(pred)
            all_true.append(gt_value)

            success_files += 1

        except Exception as e:
            fail_counter['any_error'] += 1
            fail_counter["parse_error"] += 1
            continue

    results = evaluate(all_true, all_pred)

    print(F'success/total: {success_files}/{total_files}, {float(success_files)/float(total_files)*100:.2f}%')
    print('Fail reasons:', fail_counter)
    print(pd.DataFrame([results]).T)

    for k, v in results.items():
        print(f"{k}: {v}")

if __name__ == "__main__":
    run_rule_based_pipeline('../ecg_data_test')


