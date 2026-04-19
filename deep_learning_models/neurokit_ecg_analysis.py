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
    Compute P, R, and T electrical axes.
    data_12L_dict: must include cleaned signals for 'I' (0–2.5s) and 'aVF' (2.5–5s)
    """
    axes = {}

    def get_wave_amplitudes(signal, wave_type='R'):
        # Select feature extraction strategy based on wave type
        if wave_type == 'R':
            # R-axis typically uses net QRS amplitude (Max + Min, where Min is usually the S wave)
            return np.max(signal) + np.min(signal)
        elif wave_type == 'P':
            # P-axis is usually smaller; exclude large waves and use mid-signal local maxima
            # Professional approach: use P-peak amplitude
            peaks, _ = nk.ecg_peaks(signal, sampling_rate=sampling_rate)
            # Simplified: use 95th percentile as an estimate of P-wave strength
            return np.percentile(signal, 95)
        elif wave_type == 'T':
            # T-axis uses T-peak amplitude
            return np.max(signal)
        return 0

    # Extract features from Lead I and aVF
    # Note: in 3x4+1 format, these leads are separated in time
    # Assume data_12L_dict['I'] already contains the correct segment

    for wave in ['P', 'R', 'T']:
        val_i = get_wave_amplitudes(data_12L_dict['I'], wave_type=wave)
        val_avf = get_wave_amplitudes(data_12L_dict['aVF'], wave_type=wave)

        # Compute angle using atan2(Y, X)
        angle = np.degrees(np.arctan2(val_avf, val_i))
        axes[f'{wave}_axis'] = angle

    return axes


def robust_get_peaks(signal, sampling_rate):
    """
    Robust R-peak detection: tries multiple algorithms to avoid empty output
    """
    # List of candidate methods
    methods = ["neurokit", "pantompkins1985", "nabian2018"]

    for method in methods:
        try:
            _, info = nk.ecg_peaks(signal, sampling_rate=sampling_rate, method=method)
            r_peaks = info["ECG_R_Peaks"]
            if len(r_peaks) >= 2:  # At least two peaks required for heart rate calculation
                return r_peaks, method
        except Exception:
            continue

    return np.array([]), None

def calculate_ecg_metrics(ecg, sampling_rate=500):
    results = {}

    data_long_ii = ecg['longII']

    # Preprocessing
    cleaned_long_ii = nk.ecg_clean(data_long_ii, sampling_rate=sampling_rate, method="neurokit")

    # Detect R-peaks and obtain indices
    r_peaks, method = robust_get_peaks(cleaned_long_ii, sampling_rate=sampling_rate)

    # 3. Conditional branch handling
    if r_peaks.size == 0:
        #print("Error: Unable to detect R-peaks; signal may be invalid or too noisy.")
        return results, Status.RPeakError  # Return empty result to prevent downstream crash

    peaks_dict = {"ECG_R_Peaks": r_peaks}

    # Ensure integer array to avoid string concatenation issues
    r_peaks = np.array(r_peaks, dtype=int)

    # Compute heart rate
    hr_df = nk.ecg_rate(peaks_dict, sampling_rate=sampling_rate, desired_length=len(cleaned_long_ii))  # may contain NaN
    results['Ventricular_Rate'] = np.nanmean(hr_df)  # warning if all NaN
    if np.isnan(results['Ventricular_Rate']):
        print('hr_df:', hr_df)
        print('R peaks:', r_peaks)
        return results, Status.RPeakError

    try:
        # Use corrected r_peaks for delineation
        _, waves_peak = nk.ecg_delineate(cleaned_long_ii, r_peaks,
                                         sampling_rate=sampling_rate,
                                         method="dwt")

        # Extract feature points
        def get_mean_ms(key_start, key_end):
            start = np.array(waves_peak.get(key_start, [np.nan]))
            end = np.array(waves_peak.get(key_end, [np.nan]))
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

        # QTc calculation (Bazett formula)
        rr_interval = 60 / results['Ventricular_Rate']
        results['QTc'] = qt_raw / np.sqrt(rr_interval)

    except TypeError as te:
        print(f"Type error (possibly due to NK2 internal string handling): {te}")
        return results, Status.TypeError
    except Exception as e:
        print(f"Other delineation error: {e}")
        return results, Status.DelineateError

    # --- Axis Calculation ---
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

    y_true = np.array(y_true)
    y_pred = np.array(y_pred)

    assert y_true.shape == y_pred.shape
    assert y_true.shape[1] == len(PARAM_NAMES)

    results = {}

    # =========================================
    # Per-parameter evaluation
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

            mae = mean_absolute_error(yt_num, yp_num)
            rmse = np.sqrt(mean_squared_error(yt_num, yp_num))

            if len(yt_num) > 1:
                r2 = r2_score(yt_num, yp_num)
            else:
                r2 = np.nan

            param_result["MAE"] = mae
            param_result["RMSE"] = rmse
            param_result["R2"] = r2

            # Bootstrap CI
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

        # Only meaningful if y_true contains NaN
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

            try:
                param_result["AUROC"] = roc_auc_score(yt_nan, yp_nan)
            except:
                param_result["AUROC"] = np.nan

            try:
                param_result["AUPRC"] = average_precision_score(yt_nan, yp_nan)
            except:
                param_result["AUPRC"] = np.nan

        else:
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

    # -------- Collect all txt files --------
    for root, _, files in os.walk(data_dir):
        for f in files:
            if f.endswith(".txt"):
                file_list.append(os.path.join(root, f))

    print(f"Total files: {len(file_list)}")

    all_true = []
    all_pred = []

    pbar = tqdm(file_list, desc="Extract Parameters", leave=False)

    # -------- Main loop --------
    for filepath in pbar:
        total_files += 1

        try:
            ecg, labels = parse_ecg_file(filepath)
            if np.isnan(labels['Pr']) or np.isnan(labels['Paxis']):
                print(F'Skipping {filepath} for nan in PR or P_axis')
                fail_counter['pr_paxis_nan'] += 1
                continue

            # -------- Ground Truth --------
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

                continue

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