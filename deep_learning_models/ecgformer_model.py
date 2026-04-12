import torch
import torch.nn as nn
import torch.nn.functional as F

# -----------------------------
# Patch Embedding
# -----------------------------
class PatchEmbed1D(nn.Module):
    def __init__(self, in_ch, d_model, patch_size=10):
        super().__init__()
        self.proj = nn.Conv1d(
            in_ch, d_model,
            kernel_size=patch_size,
            stride=patch_size
        )

    def forward(self, x):
        # (B, C, T) → (B, d_model, T/patch)
        x = self.proj(x)
        x = x.permute(0, 2, 1)
        return x


# -----------------------------
# CNN feature extractor
# -----------------------------
class ConvFeature(nn.Module):
    def __init__(self, in_ch, d_model):
        super().__init__()
        self.net = nn.Sequential(
            nn.Conv1d(in_ch, 64, 7, padding=3),
            nn.BatchNorm1d(64),
            nn.GELU(),

            nn.Conv1d(64, d_model, 5, padding=2),
            nn.BatchNorm1d(d_model),
            nn.GELU()
        )

    def forward(self, x):
        return self.net(x)


# -----------------------------
# Transformer Block
# -----------------------------
class TransformerBlock(nn.Module):
    def __init__(self, d_model, nhead):
        super().__init__()
        self.norm1 = nn.LayerNorm(d_model)
        self.attn = nn.MultiheadAttention(d_model, nhead, batch_first=True)

        self.norm2 = nn.LayerNorm(d_model)
        self.ff = nn.Sequential(
            nn.Linear(d_model, d_model*4),
            nn.GELU(),
            nn.Linear(d_model*4, d_model)
        )

    def forward(self, x):
        x = x + self.attn(self.norm1(x), self.norm1(x), self.norm1(x))[0]
        x = x + self.ff(self.norm2(x))
        return x


# -----------------------------
# 主模型（Dual Input）
# -----------------------------
class DualECGFormer(nn.Module):
    def __init__(self, d_model=128, nhead=4, depth=4):
        super().__init__()

        # -------- short leads --------
        self.conv = ConvFeature(12, d_model)
        self.patch = PatchEmbed1D(d_model, d_model, patch_size=10)

        # -------- long lead --------
        self.long_conv = ConvFeature(1, d_model)
        self.long_patch = PatchEmbed1D(d_model, d_model, patch_size=20)

        # -------- positional embedding --------
        self.pos_short = nn.Parameter(torch.randn(1, 200, d_model) * 0.02)
        self.pos_long = nn.Parameter(torch.randn(1, 300, d_model) * 0.02)

        # -------- transformers --------
        self.blocks_short = nn.ModuleList([
            TransformerBlock(d_model, nhead) for _ in range(depth)
        ])

        self.blocks_long = nn.ModuleList([
            TransformerBlock(d_model, nhead) for _ in range(depth)
        ])

        # -------- cross attention --------
        self.cross_attn = nn.MultiheadAttention(d_model, nhead, batch_first=True)

        # -------- heads --------
        self.fc = nn.Sequential(
            nn.Linear(d_model * 2, d_model),
            nn.GELU()
        )

        self.fc_exist = nn.Linear(d_model, 8)
        self.fc_value = nn.Linear(d_model, 8)
        self.fc_logvar = nn.Linear(d_model, 8)

    def forward(self, leads, longII):

        # ---- short leads ----
        x = self.conv(leads)
        x = self.patch(x)
        x = x + self.pos_short[:, :x.size(1), :]

        for blk in self.blocks_short:
            x = blk(x)

        # ---- long lead ----
        y = self.long_conv(longII.unsqueeze(1))
        y = self.long_patch(y)
        y = y + self.pos_long[:, :y.size(1), :]

        for blk in self.blocks_long:
            y = blk(y)

        # ---- cross attention ----
        x2, _ = self.cross_attn(x, y, y)
        y2, _ = self.cross_attn(y, x, x)

        x = x2.mean(dim=1)
        y = y2.mean(dim=1)

        feat = torch.cat([x, y], dim=1)
        feat = self.fc(feat)

        exist = torch.sigmoid(self.fc_exist(feat))
        value = self.fc_value(feat)
        logvar = torch.clamp(self.fc_logvar(feat), -3, 3)

        return exist, value, logvar