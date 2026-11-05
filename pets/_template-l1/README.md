# L1 template pack

这是给画师/作者的 L1 分层宠物包模板，不是可运行宠物。

- `CHECKLIST.md`：图层清单、顺序、枢轴、导出规则
- `canvas-guide.png`：1024×1328 画布辅助线（参考，不要导出进图层）
- `pet.json`：清单模板，图层文件名与 CHECKLIST 一致

把目录改名为 `pets/<你的包 id>`、`pet.json` 里的 `id` 改成同一个名字，补齐 `parts/*.png` 后运行：
```powershell
python tools/check-pet-pack.py pets/<你的包 id>
```
