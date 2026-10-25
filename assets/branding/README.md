# Branding

`logo.png` is the user's logo (256×256). `hbht.ico` (16–256 px) and `hbht.icns` are made from it with Pillow:

```python
from PIL import Image
im = Image.open("logo.png").convert("RGBA")
im.save("hbht.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
im.save("hbht.icns")
```

Make them again after changing the logo. The Godot projects and the installers take their icons from here: each project's `icon.png` (its `config/icon`) is a copy of `logo.png`, which `tools/package.ps1` refreshes, and the script copies the `.ico` and `.icns` beside each project for the export ([releases](../../docs/systems/releases.md)).