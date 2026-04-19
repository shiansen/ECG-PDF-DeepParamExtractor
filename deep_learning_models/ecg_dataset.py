import numpy as np
import torch
from torch.utils.data import Dataset

LEAD_ORDER = [
    "I","II","III","aVR","aVL","aVF",
    "V1","V2","V3","V4","V5","V6"
]

ALL_LEADS = LEAD_ORDER + ["longII"]

LABEL_KEYS = ["Hr","Pr","Qrs","Qt","Qtc","Paxis","Raxis","Taxis"]

MISSING_VALUES = set([10000, -10000])


def safe_float(x):
    try:
        return float(x)
    except:
        return np.nan

def parse_ecg_file(filepath):
    with open(filepath, 'r') as f:
        lines = [l.strip() for l in f if l.strip()]

    ecg = {lead: None for lead in ALL_LEADS}
    labels = {k: np.nan for k in LABEL_KEYS}

    i = 0
    while i < len(lines):
        line = lines[i]

        # -------- Lead --------
        if "," in line:
            key = line.split(",")[0]

            if key in ALL_LEADS:
                lead = key

                try:
                    # skip time
                    voltage_line = lines[i+2]
                    vals = np.array([safe_float(x) for x in voltage_line.split(",")])
                except:
                    vals = np.array([])

                ecg[lead] = vals
                i += 3
                continue

        # -------- Label --------
        if line in LABEL_KEYS:
            try:
                v = safe_float(lines[i+1])
                if v in MISSING_VALUES:
                    labels[line] = np.nan
                else:
                    labels[line] = v
            except:
                labels[line] = np.nan

            i += 2
            continue

        i += 1

    return ecg, labels


class ECGDataset(Dataset):
    def __init__(self, file_list,
                 lead_len=1250,
                 long_len=5000,
                 normalize=True,
                 label_normalizer=None):

        self.file_list = file_list
        self.lead_len = lead_len
        self.long_len = long_len
        self.normalize = normalize
        self.label_normalizer = label_normalizer

    def __len__(self):
        return len(self.file_list)

    def __getitem__(self, idx):
        ecg, labels = parse_ecg_file(self.file_list[idx])

        # -------- leads --------
        leads = []
        for lead in LEAD_ORDER:
            sig = ecg.get(lead, np.zeros(self.lead_len))
            if sig is None:
                print("error", self.file_list[idx], lead)
                raise Exception("Lead data must not None !")
            sig = self._fix_length(sig, self.lead_len)
            leads.append(sig)

        leads = np.stack(leads)  # (12,1250)

        # -------- long II --------
        longII = ecg.get("longII", np.zeros(self.long_len))
        longII = self._fix_length(longII, self.long_len)

        # -------- labels --------
        value = []
        exist = []

        for k in LABEL_KEYS:
            v = labels[k]

            if np.isnan(v):
                exist.append(0.0)
                value.append(0.0)  # dummy
            else:
                exist.append(1.0)
                value.append(v)

        value = np.array(value, dtype=np.float32)
        exist = np.array(exist, dtype=np.float32)

        # -------- Label normalization --------
        if self.label_normalizer is not None:
            # normalize
            value_norm = self.label_normalizer.transform(value)

            # important, keep existent label
            value = value_norm * exist

        return {
            "leads": torch.tensor(leads, dtype=torch.float32),
            "longII": torch.tensor(longII, dtype=torch.float32),
            "value": torch.tensor(value),
            "exist": torch.tensor(exist)
        }

    def _normalize(self, sig):
        median = np.median(sig)
        mad = np.median(np.abs(sig - median)) + 1e-6
        sig = (sig - median) / mad
        return sig

    def _fix_length(self, signal, target_len):
        if len(signal) > target_len:
            return signal[:target_len]
        elif len(signal) < target_len:
            return np.pad(signal, (0, target_len - len(signal)))
        return signal


class LabelNormalizer:
    def __init__(self):
        self.mean = None
        self.std = None

    def fit(self, dataset):
        """
        dataset: ECGDataset (train only)
        """
        values = []
        exists = []

        for i in range(len(dataset)):
            sample = dataset[i]
            values.append(sample["value"])
            exists.append(sample["exist"])

        values = np.stack(values)   # (N, 8)
        exists = np.stack(exists)   # (N, 8)

        self.mean = np.zeros(8)
        self.std = np.ones(8)

        for i in range(8):
            valid = exists[:, i] == 1

            if valid.sum() > 10:
                self.mean[i] = values[valid, i].mean()
                self.std[i] = values[valid, i].std() + 1e-6
            else:
                # fallback
                self.mean[i] = 0
                self.std[i] = 1

        print("Label mean:", self.mean)
        print("Label std :", self.std)

    def transform(self, value):
        if isinstance(value, torch.Tensor):
            mean = torch.tensor(self.mean, device=value.device)
            std = torch.tensor(self.std, device=value.device)
            return (value - mean) / std
        elif isinstance(value, np.ndarray):
            return (value - self.mean) / self.std
        else:
            raise TypeError("Unsupported type for transform")

    def inverse(self, value):
        if isinstance(value, torch.Tensor):
            mean = torch.tensor(self.mean, device=value.device, dtype=value.dtype)
            std = torch.tensor(self.std, device=value.device, dtype=value.dtype)
            return value * std + mean

        elif isinstance(value, np.ndarray):
            return value * self.std + self.mean

        else:
            raise TypeError("Unsupported type for inverse")