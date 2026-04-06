import os
import glob
import numpy as np
from sklearn.model_selection import train_test_split

def get_all_files(data_dir):
    files = glob.glob(os.path.join(data_dir, "*.txt"))
    files = sorted(files)
    print(f"Total files: {len(files)}")
    return files

def split_dataset(file_list, seed=42):

    # -------- shuffle（確保隨機性）--------
    rng = np.random.RandomState(seed)
    file_list = np.array(file_list)
    rng.shuffle(file_list)

    # -------- train / val --------
    train_files, val_files = train_test_split(
        file_list,
        test_size=0.2,
        random_state=seed,
        shuffle=True
    )

    # -------- logging --------
    print(f"Train: {len(train_files)}")
    print(f"Val: {len(val_files)}")

    return list(train_files), list(val_files)
