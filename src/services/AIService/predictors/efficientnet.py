import json
from pathlib import Path
from typing import Optional

import numpy as np
import torch
import torch.nn.functional as F
from PIL import Image

# ── Inline transforms (avoids importing from omyfish) ────────────────────────

def _get_val_transforms(image_size: int = 300):
    import albumentations as A
    from albumentations.pytorch import ToTensorV2
    return A.Compose([
        A.Resize(image_size, image_size),
        A.Normalize(mean=(0.485, 0.456, 0.406), std=(0.229, 0.224, 0.225)),
        ToTensorV2(),
    ])


# ── Inline model builder (avoids importing from omyfish) ─────────────────────

def _build_model(config: dict):
    import timm
    import torch.nn as nn

    arch = config.get("model", {}).get("architecture", "efficientnet_b3")
    num_classes = config["model"]["num_classes"]

    backbone = timm.create_model(arch, pretrained=False, num_classes=0)
    embed_dim = backbone.num_features
    dropout = config.get("model", {}).get("dropout", 0.3)

    head = nn.Sequential(
        nn.Dropout(dropout),
        nn.Linear(embed_dim, 512),
        nn.ReLU(),
        nn.Dropout(dropout),
        nn.Linear(512, num_classes),
    )

    class _Classifier(nn.Module):
        def __init__(self):
            super().__init__()
            self.backbone = backbone
            self.head = head

        def forward(self, x):
            return self.head(self.backbone(x))

    return _Classifier()


# ── Predictor ─────────────────────────────────────────────────────────────────

UNCERTAIN_THRESHOLD = 0.30


class FishPredictor:
    def __init__(self, checkpoint_path: str, classes_path: Optional[str] = None,
                 device: Optional[str] = None):
        self.device = device or ("cuda" if torch.cuda.is_available() else "cpu")

        ckpt = torch.load(checkpoint_path, map_location=self.device, weights_only=False)
        config = ckpt["config"]

        if classes_path is None:
            classes_path = str(Path(checkpoint_path).parent / "classes.json")
        self.classes = json.loads(Path(classes_path).read_text())
        config["model"]["num_classes"] = len(self.classes)

        self.model = _build_model(config).to(self.device)
        self.model.load_state_dict(ckpt["model_state_dict"])
        self.model.eval()

        image_size = config.get("data", {}).get("image_size", 300)
        self.transform = _get_val_transforms(image_size)

    @torch.no_grad()
    def predict(self, image: Image.Image, top_k: int = 5) -> dict:
        arr = np.array(image.convert("RGB"))
        tensor = self.transform(image=arr)["image"].unsqueeze(0).to(self.device)

        probs = F.softmax(self.model(tensor), dim=1)[0]
        top_probs, top_idx = probs.topk(min(top_k, len(self.classes)))

        predictions = []
        for prob, idx in zip(top_probs.tolist(), top_idx.tolist()):
            name = self.classes[idx]
            predictions.append({
                "species": name,
                "confidence": round(prob, 4),
            })

        uncertain = not predictions or predictions[0]["confidence"] < UNCERTAIN_THRESHOLD
        return {
            "predictions": predictions,
            "uncertain": uncertain,
        }
