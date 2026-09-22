# UI 字体

来源：[Noto CJK Sans2.004](https://github.com/notofonts/noto-cjk/tree/Sans2.004)，SIL Open Font License 1.1（同目录 `OFL.txt`）。原字体版权信息保留在字体 name 表中。

- `ChromaUI-SC.otf`：由 `Sans/OTF/SimplifiedChinese/NotoSansCJKsc-Regular.otf` 提取。
- `ChromaUI-JP.otf`：由 `Sans/OTF/Japanese/NotoSansCJKjp-Regular.otf` 提取。

两份修改后的字体均改名为 ChromaUI，保留三语文案、语言名称、ASCII 和操作符号。原文件不提交仓库；下载对应版本后分别保存为 `.local/fonts/sc.otf`、`.local/fonts/jp.otf`。

安装 Python `fonttools` 后执行：

```sh
python3 tools/subset_fonts.py .local/fonts
```

修改翻译后如出现新字符，必须重新生成子集。网页分发字体和许可证；桌面 ZIP 附带 `FONT-LICENSE.txt` 与 `FONT-SOURCES.md`。
