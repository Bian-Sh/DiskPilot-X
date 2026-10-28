"""只检查实际弹窗标题区域，避免把背景中的红色清理按钮误判为标题色。"""
import json
import sys
from pathlib import Path
from PIL import Image


def verify(directory: Path):
    evidence = json.loads((directory / "balanced-ui-evidence.json").read_text(encoding="utf-8-sig"))
    kinds = {"warning": "cleanup-single", "danger": "cleanup-permanent", "normal": "modal-info"}
    results = []
    for severity, color in [("warning", (134, 113, 79)), ("danger", (187, 33, 36)), ("normal", (98, 104, 110))]:
        for theme in ["light", "dark"]:
            item = next(item for item in evidence if item.get("theme") == theme and item["kind"] == kinds[severity])
            metrics = item["metrics"]
            image = Image.open(directory / item["screenshot"]).convert("RGB")
            sx = image.width / metrics.get("windowWidth", image.width)
            sy = image.height / metrics.get("windowHeight", image.height)
            left = (image.width - metrics["width"] * sx) / 2
            top = (image.height - metrics["height"] * sy) / 2
            center_y = top + metrics["headerHeight"] * sy / 2
            region = image.crop((round(left + 24 * sx), round(center_y - 4 * sy),
                                 round(left + 90 * sx), round(center_y + 4 * sy)))
            count = next((count for count, value in region.getcolors(region.width * region.height) if value == color), 0)
            assert count >= region.width * region.height * .9, f"标题底色不符：{severity}/{theme}，期望 {color}"
            results.append({"severity": severity, "theme": theme, "rgb": color, "pixels": count,
                            "sample_size": region.size, "header_color": metrics["headerColor"]})
    return results


if __name__ == "__main__":
    print(json.dumps(verify(Path(sys.argv[1])), ensure_ascii=False, indent=2))
