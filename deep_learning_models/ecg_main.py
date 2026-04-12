import os
from tqdm import tqdm
import time
import torch
import numpy as np
from torch.utils.data import DataLoader
from sklearn.metrics import (
    accuracy_score, precision_score, recall_score, f1_score,
    roc_auc_score, average_precision_score, confusion_matrix,
    r2_score, roc_curve, precision_recall_curve
)
import matplotlib.pyplot as plt
import pickle
from load_data import get_all_files
from ecg_dataset import ECGDataset, LabelNormalizer
from load_data import split_dataset
from resnet_model import DualResNetECG, loss_fn_resnet
from ecgformer_model import DualECGFormer

# ====== CONFIG ======
BATCH_SIZE = 256
LR = 1e-4
EPOCHS = 5
PATIENCE = 10

LABEL_NAMES = ["HR","PR","QRS","QT","QTc","Paxis","Raxis","Taxis"]

def compute_exist_metrics2(pred_exist, gt_exist):
    results = {}

    pred_bin = (pred_exist > 0.5).astype(int)

    for i, name in enumerate(LABEL_NAMES):
        y_true = gt_exist[:, i]
        y_pred = pred_bin[:, i]
        y_prob = pred_exist[:, i]

        if len(np.unique(y_true)) < 2:  # only PR and Paxis
            continue

        acc = accuracy_score(y_true, y_pred)
        prec = precision_score(y_true, y_pred, zero_division=0)  # = PPV
        rec = recall_score(y_true, y_pred, zero_division=0)

        f1 = f1_score(y_true, y_pred, zero_division=0)

        # confusion matrix
        tn, fp, fn, tp = confusion_matrix(y_true, y_pred).ravel()

        # -------------------------
        # clinical metrics
        # -------------------------
        spec = tn / (tn + fp) if (tn + fp) > 0 else np.nan
        npv = tn / (tn + fn) if (tn + fn) > 0 else np.nan

        # -------------------------
        # AUROC
        # -------------------------
        try:
            auc = roc_auc_score(y_true, y_prob)
        except:
            auc = np.nan

        # -------------------------
        # AUPRC
        # -------------------------
        try:
            auprc = average_precision_score(y_true, y_prob)
        except:
            auprc = np.nan

        results[name] = {
            "Accuracy": acc,
            "Precision": prec,   # same as PPV
            "Recall": rec,
            "Specificity": spec,
            "F1": f1,
            "NPV": npv,        # 預測「沒有 P wave」
            "AUROC": auc,
            "AUPRC": auprc
        }

    return results

def compute_exist_metrics(pred_exist, gt_exist):
    results = {}

    pred_bin = (pred_exist > 0.5).astype(int)

    # 👉 只關心 PR (index=1) & Paxis (index=5)
    TARGET_INDEX = {
        "PR": 1,
        "Paxis": 5
    }

    for name, i in TARGET_INDEX.items():

        y_true = gt_exist[:, i]
        y_pred = pred_bin[:, i]
        y_prob = pred_exist[:, i]

        # -------------------------
        # Classification metrics
        # -------------------------
        acc = accuracy_score(y_true, y_pred)
        prec = precision_score(y_true, y_pred, zero_division=0)
        rec = recall_score(y_true, y_pred, zero_division=0)
        f1 = f1_score(y_true, y_pred, zero_division=0)

        tn, fp, fn, tp = confusion_matrix(y_true, y_pred).ravel()

        spec = tn / (tn + fp) if (tn + fp) > 0 else np.nan
        npv = tn / (tn + fn) if (tn + fn) > 0 else np.nan

        # -------------------------
        # AUROC / AUPRC
        # -------------------------
        valid_mask = ~np.isnan(y_true) & ~np.isnan(y_prob)

        if valid_mask.sum() > 10:  # 👉 至少要有樣本數

            yt = y_true[valid_mask]
            yp = y_prob[valid_mask]

            if len(np.unique(yt)) > 1:
                try:
                    auc = roc_auc_score(yt, yp)
                except:
                    auc = np.nan

                try:
                    auprc = average_precision_score(yt, yp)
                except:
                    auprc = np.nan
            else:
                auc = np.nan
                auprc = np.nan

        else:
            auc = np.nan
            auprc = np.nan

        results[name] = {
            "Accuracy": acc,
            "Precision": prec,
            "Recall": rec,
            "Specificity": spec,
            "F1": f1,
            "NPV": npv,
            "AUROC": auc,
            "AUPRC": auprc,

            # 👉 加這個（後面畫圖會用）
            "y_true": y_true,
            "y_prob": y_prob
        }

    return results

def plot_exist_curves(exist_metrics, save_dir=None):

    for name, data in exist_metrics.items():

        if name == "PR":
            name = "PR interval :"
        if name == "Paxis":
            name = "P axis :"

        y_true = data["y_true"]
        y_prob = data["y_prob"]

        # -------------------------
        # 🔥 關鍵：移除 NaN
        # -------------------------
        valid_mask = ~np.isnan(y_true) & ~np.isnan(y_prob)

        if valid_mask.sum() < 10:
            print(f"{name}: not enough valid samples after NaN removal")
            continue

        yt = y_true[valid_mask]
        yp = y_prob[valid_mask]

        # -------------------------
        # 必須有兩類
        # -------------------------
        if len(np.unique(yt)) < 2:
            print(f"{name}: only one class after filtering, skip")
            continue

        # =========================
        # ROC curve
        # =========================
        try:
            fpr, tpr, _ = roc_curve(yt, yp)
        except Exception as e:
            print(f"{name}: ROC failed - {e}")
            continue

        plt.figure()
        plt.plot(fpr, tpr)
        plt.xlabel("False Positive Rate")
        plt.ylabel("True Positive Rate")
        plt.title(f"{name} ROC Curve (AUROC={data.get('AUROC', np.nan):.3f})")

        if save_dir:
            plt.savefig(f"{save_dir}/{name}_ROC.png", dpi=300)

        plt.show()

        # =========================
        # PR curve
        # =========================
        try:
            precision, recall, _ = precision_recall_curve(yt, yp)
        except Exception as e:
            print(f"{name}: PR curve failed - {e}")
            continue

        plt.figure()
        plt.plot(recall, precision)
        plt.xlabel("Recall")
        plt.ylabel("Precision")
        plt.title(f"{name} PR Curve (AUPRC={data.get('AUPRC', np.nan):.3f})")

        if save_dir:
            plt.savefig(f"{save_dir}/{name}_PR.png", dpi=300)

        plt.show()

# =========================
# Bootstrap CI
# =========================
def bootstrap_ci(y_true, y_pred, func, n_boot=1000, alpha=0.05):

    n = len(y_true)
    stats = []

    for _ in range(n_boot):
        idx = np.random.randint(0, n, n)
        yt = y_true[idx]
        yp = y_pred[idx]

        try:
            stats.append(func(yt, yp))
        except:
            continue

    if len(stats) == 0:
        return np.nan, np.nan

    lower = np.percentile(stats, 100 * (alpha/2))
    upper = np.percentile(stats, 100 * (1 - alpha/2))

    return lower, upper


# =========================
# 主函式（升級版）
# =========================
def compute_value_metrics(pred, gt, exist, n_boot=1000):

    results = {}

    for i, name in enumerate(LABEL_NAMES):

        mask_exist = exist[:, i] == 1
        if mask_exist.sum() < 5:
            continue

        y_true = gt[:, i]
        y_pred = pred[:, i]

        valid_mask = (
            mask_exist &
            ~np.isnan(y_true) &
            ~np.isnan(y_pred)
        )

        if valid_mask.sum() < 5:
            continue

        yt = y_true[valid_mask]
        yp = y_pred[valid_mask]

        # -------------------------
        # 基本 metrics
        # -------------------------
        errors = yt - yp

        mae = np.mean(np.abs(errors))
        rmse = np.sqrt(np.mean(errors**2))
        medae = np.median(np.abs(errors))

        bias = np.mean(errors)
        std = np.std(errors)

        r2 = r2_score(yt, yp) if len(yt) > 1 else np.nan

        # -------------------------
        # Bootstrap CI
        # -------------------------
        mae_ci = bootstrap_ci(yt, yp, lambda a, b: np.mean(np.abs(a - b)), n_boot)
        rmse_ci = bootstrap_ci(yt, yp, lambda a, b: np.sqrt(np.mean((a - b)**2)), n_boot)

        def safe_r2(a, b):
            return r2_score(a, b) if len(a) > 1 else np.nan

        r2_ci = bootstrap_ci(yt, yp, safe_r2, n_boot)

        # -------------------------
        # 結果整理
        # -------------------------
        results[name] = {
            "MAE": mae,
            "MAE_CI95": mae_ci,

            "RMSE": rmse,
            "RMSE_CI95": rmse_ci,

            "MedAE": medae,

            "Bias": bias,
            "StdError": std,

            "R2": r2,
            "R2_CI95": r2_ci,

            "N_total_exist": int(mask_exist.sum()),
            "N_used": int(valid_mask.sum()),
            "N_dropped_nan": int(mask_exist.sum() - valid_mask.sum())
        }

    return results

def compute_value_metrics3(pred, gt, exist):
    results = {}

    for i, name in enumerate(LABEL_NAMES):

        # -------------------------
        # 1. 基本 mask（應該存在）
        # -------------------------
        mask_exist = exist[:, i] == 1

        if mask_exist.sum() < 5:
            continue

        y_true = gt[:, i]
        y_pred = pred[:, i]

        # -------------------------
        # 2. 移除 NaN（關鍵修正）
        # -------------------------
        valid_mask = (
            mask_exist &
            ~np.isnan(y_true) &
            ~np.isnan(y_pred)
        )

        if valid_mask.sum() < 5:
            continue

        yt = y_true[valid_mask]
        yp = y_pred[valid_mask]

        # -------------------------
        # 3. metrics
        # -------------------------
        mae = np.mean(np.abs(yt - yp))
        rmse = np.sqrt(np.mean((yt - yp) ** 2))

        # R2 needs >1 sample
        if len(yt) > 1:
            r2 = r2_score(yt, yp)
        else:
            r2 = np.nan

        # -------------------------
        # 4. 統計資訊（建議保留）
        # -------------------------
        results[name] = {
            "MAE": mae,
            "RMSE": rmse,
            "R2": r2,
            "N_total_exist": int(mask_exist.sum()),
            "N_used": int(valid_mask.sum()),
            "N_dropped_nan": int(mask_exist.sum() - valid_mask.sum())
        }

    return results

def compute_value_metrics2(pred, gt, exist):
    results = {}

    for i, name in enumerate(LABEL_NAMES):
        mask = exist[:, i] == 1

        if mask.sum() < 5:
            continue

        y_true = gt[mask, i]
        y_pred = pred[mask, i]

        mae = np.mean(np.abs(y_true - y_pred))
        rmse = np.sqrt(np.mean((y_true - y_pred) ** 2))

        from sklearn.metrics import r2_score
        r2 = r2_score(y_true, y_pred)

        results[name] = {
            "MAE": mae,
            "RMSE": rmse,
            "R2": r2,
            "N": int(mask.sum())
        }

    return results

def compute_joint_metrics(pred_exist, gt_exist):
    results = {}

    pred_bin = (pred_exist > 0.5).astype(int)

    for i, name in enumerate(LABEL_NAMES):
        y_true = gt_exist[:, i]
        y_pred = pred_bin[:, i]

        TP = np.sum((y_true == 1) & (y_pred == 1))
        TN = np.sum((y_true == 0) & (y_pred == 0))
        FP = np.sum((y_true == 0) & (y_pred == 1))
        FN = np.sum((y_true == 1) & (y_pred == 0))

        results[name] = {
            "TP": int(TP),
            "TN": int(TN),
            "FP": int(FP),
            "FN": int(FN),
            "Miss_rate": FN / (TP + FN + 1e-6),  # clinically重要
            "False_alarm": FP / (TN + FP + 1e-6)
        }

    return results

def forward_model(model, leads, longII, model_name):
    if model_name == "resnet":
        pred_exist, pred_value, _ = model(leads, longII)

    elif model_name == "ecgformer":
        pred_exist, pred_value, _ = model(leads, longII)

    else:
        raise ValueError("Unknown model")

    return pred_exist, pred_value

def bland_altman_plot(
    y_true,
    y_pred,
    title="Bland–Altman Plot",
    xlabel="Mean of True and Predicted",
    ylabel="Difference (True - Predicted)",
    save_path=None
):
    """
    Bland–Altman plot (clinical-grade)

    Parameters
    ----------
    y_true : array-like
    y_pred : array-like
    save_path : str or None (if provided, save figure)
    """

    y_true = np.array(y_true)
    y_pred = np.array(y_pred)

    # -------------------------
    # Remove NaN
    # -------------------------
    mask = ~np.isnan(y_true) & ~np.isnan(y_pred)
    y_true = y_true[mask]
    y_pred = y_pred[mask]

    if len(y_true) < 5:
        raise ValueError("Not enough valid samples for Bland–Altman plot")

    # -------------------------
    # Compute statistics
    # -------------------------
    mean = (y_true + y_pred) / 2
    diff = y_true - y_pred

    bias = np.mean(diff)
    std = np.std(diff)

    loa_upper = bias + 1.96 * std
    loa_lower = bias - 1.96 * std

    # proportion within LoA
    within_loa = np.mean((diff >= loa_lower) & (diff <= loa_upper))

    # -------------------------
    # Plot
    # -------------------------
    plt.figure()

    plt.scatter(mean, diff)

    plt.axhline(bias)
    plt.axhline(loa_upper)
    plt.axhline(loa_lower)

    plt.xlabel(xlabel)
    plt.ylabel(ylabel)
    plt.title(title)

    # annotations
    x_min = np.min(mean)

    plt.text(x_min, bias, f"Bias = {bias:.2f}")
    plt.text(x_min, loa_upper, f"+1.96 SD = {loa_upper:.2f}")
    plt.text(x_min, loa_lower, f"-1.96 SD = {loa_lower:.2f}")

    plt.text(
        x_min,
        loa_lower - 0.05 * (loa_upper - loa_lower),
        f"Within LoA: {within_loa*100:.1f}%"
    )

    plt.grid()

    # -------------------------
    # Save if needed
    # -------------------------
    if save_path is not None:
        plt.savefig(save_path, dpi=300, bbox_inches="tight")

    plt.show()

    # -------------------------
    # Return stats
    # -------------------------
    return {
        "bias": bias,
        "std": std,
        "loa_upper": loa_upper,
        "loa_lower": loa_lower,
        "within_loa": within_loa,
        "n": len(y_true)
    }

def final_test(model, test_loader, device, model_name, label_norm=None):
    model.eval()

    preds, exists_pred = [], []
    gts, exists_gt = [], []

    start_time = time.time()

    pbar = tqdm(test_loader, desc="Test", leave=False)

    with torch.no_grad():
        for batch in pbar:
            leads = batch["leads"].to(device)
            longII = batch["longII"].to(device)

            value = batch["value"].cpu().numpy()
            exist = batch["exist"].cpu().numpy()

            # 🔥 unified forward
            pred_exist, pred_value = forward_model(model, leads, longII, model_name)

            # 🔥 inverse normalization
            if label_norm is not None:
                pred_value = label_norm.inverse(pred_value)
                value = label_norm.inverse(value)

            preds.append(pred_value.cpu().numpy())
            exists_pred.append(pred_exist.cpu().numpy())

            gts.append(value)
            exists_gt.append(exist)

    epoch_time = time.time() - start_time

    preds = np.concatenate(preds)
    gts = np.concatenate(gts)
    exists_gt = np.concatenate(exists_gt)
    exists_pred = np.concatenate(exists_pred)

    # -------- metrics --------
    value_metrics = compute_value_metrics(preds, gts, exists_gt)
    exist_metrics = compute_exist_metrics(exists_pred, exists_gt)
    joint_metrics = compute_joint_metrics(exists_pred, exists_gt)

    print("\n===== FINAL TEST RESULTS =====")
    for k, v in value_metrics.items():
        print(k, v)
    for k, v in exist_metrics.items():
        print(k, v)
    for k, v in joint_metrics.items():
        print(k, v)

    plot_exist_curves(exist_metrics, save_dir="plots")
    print("PR exist distribution:", np.bincount(exists_gt[:, 1].astype(int)))
    print("Paxis exist distribution:", np.bincount(exists_gt[:, 5].astype(int)))


def build_model(model_name, device):

    if model_name.lower() == "resnet":
        model = DualResNetECG().to(device)
        loss_fn = loss_fn_resnet
        use_pretrain = False

    elif model_name.lower() == "ecgformer":
        model = DualECGFormer().to(device)
        loss_fn = loss_fn_resnet
        use_pretrain = False

    else:
        raise ValueError(f"Unknown model: {model_name}")

    return model, loss_fn, use_pretrain

def get_checkpoint_path(model_name):
    return f"best_model_{model_name}.pth"

def train_epoch(model, loader, optimizer, device, loss_fn, model_name):
    model.train()
    total_loss = 0

    start_time = time.time()

    pbar = tqdm(loader, desc="Train", leave=False)

    for batch in pbar:
        leads = batch["leads"].to(device)
        longII = batch["longII"].to(device)
        value = batch["value"].to(device)
        exist = batch["exist"].to(device)

        exist_p, value_p, logvar = model(leads, longII)
        loss = loss_fn(exist_p, value_p, logvar, exist, value)

        optimizer.zero_grad()
        loss.backward()
        optimizer.step()

        total_loss += loss.item()

        pbar.set_postfix(loss=f"{loss.item():.4f}")

    epoch_time = time.time() - start_time

    return total_loss / len(loader), epoch_time

def evaluate(model, loader, device, model_name, label_norm):
    model.eval()

    preds, exists_pred = [], []
    gts, exists_gt = [], []

    start_time = time.time()

    pbar = tqdm(loader, desc="Eval", leave=False)

    with torch.no_grad():
        for batch in pbar:
            leads = batch["leads"].to(device)
            longII = batch["longII"].to(device)

            value = batch["value"].cpu().numpy()
            exist = batch["exist"].cpu().numpy()

            pred_exist, pred_value, _ = model(leads, longII)

            # 🔥 inverse normalization
            pred_value = label_norm.inverse(pred_value)
            value = label_norm.inverse(value)

            preds.append(pred_value.cpu().numpy())
            exists_pred.append(pred_exist.cpu().numpy())

            gts.append(value)
            exists_gt.append(exist)

    epoch_time = time.time() - start_time

    preds = np.concatenate(preds)
    gts = np.concatenate(gts)
    exists_gt = np.concatenate(exists_gt)
    exists_pred = np.concatenate(exists_pred)

    # -------- metrics --------
    value_metrics = compute_value_metrics(preds, gts, exists_gt)
    exist_metrics = compute_exist_metrics(exists_pred, exists_gt)
    joint_metrics = compute_joint_metrics(exists_pred, exists_gt)

    return value_metrics, exist_metrics, joint_metrics, epoch_time

def main(model_name, test_only=False):
    # ===== CONFIG =====
    MODEL_NAME = model_name   # 🔥 改這裡切換模型
    # 指定使用第1張顯卡 (索引為 0)
    device = torch.device("cuda:1") if torch.cuda.is_available() else "cpu"
    MODEL_PATH = get_checkpoint_path(MODEL_NAME)

    print(f"\n===== Using model: {MODEL_NAME} =====")

    # -------- load files --------
    print('Loading train data...')
    train_data_dir = "../ecg_data_train"
    train_val_files = get_all_files(train_data_dir)

    # -------- split --------
    train_files, val_files = split_dataset(train_val_files)

    print('Loading test data...')
    test_data_dir = "../ecg_data_test"
    test_files = get_all_files(test_data_dir)
    print(f"Test: {len(test_files)}")

    # -------- dataset --------
    if os.path.exists("label_norm.pkl"):
        print("Load label normalizer...")
        with open("label_norm.pkl", "rb") as f:
            label_norm = pickle.load(f)
    else:
        print("Fit label normalizer...")
        train_ds_raw = ECGDataset(train_files, label_normalizer=None)
        label_norm = LabelNormalizer()
        label_norm.fit(train_ds_raw)

        # save
        with open("label_norm.pkl", "wb") as f:
            pickle.dump(label_norm, f)

    train_ds = ECGDataset(train_files, label_normalizer=label_norm)
    val_ds = ECGDataset(val_files, label_normalizer=label_norm)
    test_ds = ECGDataset(test_files, label_normalizer=label_norm)

    train_loader = DataLoader(train_ds, batch_size=BATCH_SIZE, shuffle=True)
    val_loader   = DataLoader(val_ds, batch_size=BATCH_SIZE)
    test_loader  = DataLoader(test_ds, batch_size=BATCH_SIZE)

    # -------- model --------
    model, loss_fn, use_pretrain = build_model(MODEL_NAME, device)
    optimizer = torch.optim.Adam(model.parameters(), lr=LR)

    best_val_mae = float("inf")
    start_epoch = 0

    # ===== LOAD =====
    if os.path.exists(MODEL_PATH):
        print(f"Loading checkpoint: {MODEL_PATH}")
        checkpoint = torch.load(MODEL_PATH, map_location=device, weights_only=False)

        model.load_state_dict(checkpoint["model"])
        optimizer.load_state_dict(checkpoint["optimizer"])
        best_val_mae = checkpoint["best_val_mae"]

    if not test_only:
        # ===== FINETUNE =====
        print("\n===== FINETUNE =====")

        patience_counter = 0

        for epoch in range(EPOCHS):

            train_loss, train_time = train_epoch(
                model, train_loader, optimizer, device, loss_fn, MODEL_NAME
            )

            value_metrics, exist_metrics, joint_metrics, val_time = evaluate(
                model, val_loader, device, MODEL_NAME, label_norm
            )

            val_mae = np.mean([v["MAE"] for v in value_metrics.values()])

            print(
                f"[Epoch {epoch+1}] "
                f"Train loss: {train_loss:.4f} ({train_time:.1f}s) | "
                f"Val MAE: {val_mae:.4f} ({val_time:.1f}s)"
            )
            for k, v in value_metrics.items():
                print(k, v)
            for k, v in exist_metrics.items():
                print(k, v)
            for k, v in joint_metrics.items():
                print(k, v)


            if val_mae < best_val_mae:
                print("🔥 New best model! Saving...")
                best_val_mae = val_mae
                patience_counter = 0

                torch.save({
                    "model": model.state_dict(),
                    "optimizer": optimizer.state_dict(),
                    "best_val_mae": best_val_mae,
                    "model_name": MODEL_NAME
                }, MODEL_PATH)

            else:
                patience_counter += 1
                print(f"Patience: {patience_counter}/{PATIENCE}")

                if patience_counter >= PATIENCE:
                    print("⛔ Early stopping")
                    break

    # ===== FINAL TEST =====
    print("\nLoading best model...")
    checkpoint = torch.load(MODEL_PATH, map_location=device, weights_only=False)
    model.load_state_dict(checkpoint["model"])

    final_test(model, test_loader, device, MODEL_NAME, label_norm)

if __name__ == "__main__":
    MODEL_NAME = "resnet" # "resnet", "ecgformer" 🔥 改這裡切換模型
    main(MODEL_NAME, test_only=True)
