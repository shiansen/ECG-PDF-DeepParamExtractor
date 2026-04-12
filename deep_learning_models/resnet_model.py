import torch
import torch.nn as nn
import torch.nn.functional as F

class SEBlock(nn.Module):
    def __init__(self, ch, reduction=8):
        super().__init__()
        self.pool = nn.AdaptiveAvgPool1d(1)
        self.fc = nn.Sequential(
            nn.Linear(ch, ch // reduction),
            nn.ReLU(inplace=True),
            nn.Linear(ch // reduction, ch),
            nn.Sigmoid()
        )

    def forward(self, x):
        w = self.pool(x).squeeze(-1)
        w = self.fc(w).unsqueeze(-1)
        return x * w

class ResBlock1D(nn.Module):
    def __init__(self, in_ch, out_ch, stride=1, dilation=1, use_se=True):
        super().__init__()

        padding = dilation

        self.conv1 = nn.Conv1d(
            in_ch, out_ch, kernel_size=3,
            stride=stride, padding=padding,
            dilation=dilation, bias=False
        )
        self.bn1 = nn.BatchNorm1d(out_ch)

        self.conv2 = nn.Conv1d(
            out_ch, out_ch, kernel_size=3,
            padding=padding, dilation=dilation,
            bias=False
        )
        self.bn2 = nn.BatchNorm1d(out_ch)

        self.relu = nn.ReLU(inplace=True)

        self.use_se = use_se
        if use_se:
            self.se = SEBlock(out_ch)

        self.downsample = None
        if stride != 1 or in_ch != out_ch:
            self.downsample = nn.Sequential(
                nn.Conv1d(in_ch, out_ch, kernel_size=1,
                          stride=stride, bias=False),
                nn.BatchNorm1d(out_ch)
            )

    def forward(self, x):
        identity = x

        out = self.relu(self.bn1(self.conv1(x)))
        out = self.bn2(self.conv2(out))

        if self.use_se:
            out = self.se(out)

        if self.downsample is not None:
            identity = self.downsample(x)

        out += identity
        return self.relu(out)

class ResNet1D(nn.Module):
    def __init__(self, in_ch, base_ch=64, d_model=256):
        super().__init__()

        self.stem = nn.Sequential(
            nn.Conv1d(in_ch, base_ch, kernel_size=7, stride=2, padding=3, bias=False),
            nn.BatchNorm1d(base_ch),
            nn.ReLU(inplace=True)
        )

        # ---- stages ----
        self.layer1 = nn.Sequential(
            ResBlock1D(base_ch, base_ch, dilation=1),
            ResBlock1D(base_ch, base_ch, dilation=1)
        )

        self.layer2 = nn.Sequential(
            ResBlock1D(base_ch, base_ch*2, stride=2, dilation=1),
            ResBlock1D(base_ch*2, base_ch*2, dilation=2)
        )

        self.layer3 = nn.Sequential(
            ResBlock1D(base_ch*2, base_ch*4, stride=2, dilation=2),
            ResBlock1D(base_ch*4, base_ch*4, dilation=4)
        )

        self.layer4 = nn.Sequential(
            ResBlock1D(base_ch*4, d_model, stride=2, dilation=2),
            ResBlock1D(d_model, d_model, dilation=4)
        )

        self.pool = nn.AdaptiveAvgPool1d(1)

    def forward(self, x):
        x = self.stem(x)
        x = self.layer1(x)
        x = self.layer2(x)
        x = self.layer3(x)
        x = self.layer4(x)
        x = self.pool(x).squeeze(-1)
        return x

# -----------------------------
# 主模型（Dual Input）
# -----------------------------
class DualResNetECG(nn.Module):
    def __init__(self, d_model=256):
        super().__init__()

        # -------- short leads branch --------
        self.conv = ResNet1D(in_ch=12, d_model=d_model)

        # -------- long lead branch --------
        self.long_conv = ResNet1D(in_ch=1, d_model=d_model)

        # -------- feature fusion --------
        self.fusion = nn.Sequential(
            nn.Linear(d_model * 2, d_model),
            nn.ReLU(inplace=True),
            nn.Dropout(0.2)
        )

        # -------- heads --------
        self.fc_exist = nn.Linear(d_model, 8)
        self.fc_value = nn.Linear(d_model, 8)
        self.fc_logvar = nn.Linear(d_model, 8)

    def forward(self, leads, longII):
        """
        leads:  (B, 12, 1250)
        longII: (B, 5000)
        """

        # ---- short leads ----
        x = self.conv(leads)  # (B, d_model)

        # ---- long lead ----
        longII = longII.unsqueeze(1)
        y = self.long_conv(longII)  # (B, d_model)

        # ---- fusion ----
        feat = torch.cat([x, y], dim=1)
        feat = self.fusion(feat)

        # ---- heads ----
        exist = torch.sigmoid(self.fc_exist(feat))
        value = self.fc_value(feat)
        logvar = torch.clamp(self.fc_logvar(feat), -3, 3)

        return exist, value, logvar

def loss_fn_resnet(
    exist_pred,
    value_pred,
    logvar,
    exist_gt,
    value_gt,
    lambda_exist=1.0,
    lambda_value=1.0
):
    """
    Multi-task loss:
    - existence (classification)
    - value (heteroscedastic regression)
    """

    # -------- sanity check --------
    assert exist_pred.shape == exist_gt.shape
    assert value_pred.shape == value_gt.shape

    if torch.isnan(exist_pred).any():
        raise ValueError("exist_pred has NaN")
    if torch.isnan(value_pred).any():
        raise ValueError("value_pred has NaN")

    # -------- clamp uncertainty（🔥關鍵）--------
    logvar = torch.clamp(logvar, -3, 3)

    # -------- existence loss --------
    bce = F.binary_cross_entropy(exist_pred, exist_gt)

    # -------- regression loss（Huber + uncertainty）--------
    diff = value_pred - value_gt

    # Huber loss（比 MSE 穩定）
    huber = F.smooth_l1_loss(value_pred, value_gt, reduction='none')

    precision = torch.exp(-logvar)

    reg = precision * huber + logvar

    # -------- mask（只算存在的）--------
    reg = reg * exist_gt

    # -------- ⭐ label weighting --------
    weights = torch.tensor(
        [1.0, 1.0, 1.0, 1.2, 1.5, 0.5, 0.5, 0.5],  # 👉 QT / QTc 更重要（臨床）
        device=value_gt.device
    )
    reg = reg * weights  # (B, 8)

    denom = exist_gt.sum()

    if denom < 1:
        reg = reg.mean()
    else:
        reg = reg.sum() / (denom + 1e-6)

    # -------- total loss --------
    loss = lambda_exist * bce + lambda_value * reg

    return loss