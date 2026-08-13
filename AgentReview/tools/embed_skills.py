# skills/design-review/ の正本を main.cs の DesignReviewSkillFiles（自動生成領域）へ
# C# verbatim 文字列として埋め込む。スキルを編集したらこのスクリプトを実行し、
# Next Design を再起動して反映する。
#
# 使い方: python AgentReview/tools/embed_skills.py
import io
import os

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))  # AgentReview/
MAIN = os.path.join(BASE, "main.cs")
SKILL_DIR = os.path.join(BASE, "skills", "design-review")

BEGIN = "// ---- BEGIN GENERATED SKILL FILES"
END = "// ---- END GENERATED SKILL FILES ----"

FILES = [
    ("SkillMd", "SKILL.md"),
    ("RequirementsMd", os.path.join("references", "requirements-review.md")),
    ("ArchitectureMd", os.path.join("references", "architecture-review.md")),
    ("DetailedDesignMd", os.path.join("references", "detailed-design-review.md")),
]


def verbatim(text):
    # C# verbatim 文字列: " を "" に。改行はそのまま入れられる
    return '@"' + text.replace('"', '""') + '"'


def main():
    body = ["// ---- BEGIN GENERATED SKILL FILES (tools/embed_skills.py が skills/design-review から生成。手で編集しない) ----"]
    body.append("public static class DesignReviewSkillFiles")
    body.append("{")
    for i, (const_name, rel) in enumerate(FILES):
        text = io.open(os.path.join(SKILL_DIR, rel), encoding="utf-8").read()
        text = text.replace("\r\n", "\n")
        if i > 0:
            body.append("")
        body.append("    public const string " + const_name + " = " + verbatim(text) + ";")
    body.append("}")
    body.append(END)

    src = io.open(MAIN, encoding="utf-8").read()
    begin_pos = src.index(BEGIN)
    end_pos = src.index(END) + len(END)
    out = src[:begin_pos] + "\n".join(body) + src[end_pos:]
    io.open(MAIN, "w", encoding="utf-8", newline="").write(out)
    print("embedded %d skill files into main.cs (%d chars)" % (len(FILES), end_pos - begin_pos))


if __name__ == "__main__":
    main()
