import argparse
import html
import json
from pathlib import Path


def escape(value):
    return html.escape(str(value), quote=True)


def exercise_html(exercise):
    kind = exercise["kind"]
    labels = {
        "choice": "Scelta multipla",
        "matching": "Abbinamento",
        "guidedTranslation": "Traduzione guidata",
        "fillBlank": "Completamento",
    }
    extra = ""
    if kind in ("choice", "fillBlank"):
        if kind == "fillBlank":
            extra += f'<p class="phrase">{escape(exercise["textWithGap"])}</p>'
        extra += "<ol>" + "".join(
            f'<li>{escape(option)}{" <strong>(soluzione proposta)</strong>" if index == exercise["correctOptionIndex"] else ""}</li>'
            for index, option in enumerate(exercise["options"])
        ) + "</ol>"
    elif kind == "matching":
        extra = "<ul>" + "".join(
            f'<li>{escape(pair["left"])} &rarr; {escape(pair["right"])}</li>'
            for pair in exercise["pairs"]
        ) + "</ul>"
    elif kind == "guidedTranslation":
        extra = f'<p><b>Tessere:</b> {escape(" | ".join(exercise["tokens"]))}</p>'
        extra += f'<p><b>Soluzione proposta:</b> {escape(" ".join(exercise["answerTokens"]))}</p>'
    return (
        f'<article class="exercise"><span>{escape(exercise["id"])} · {labels[kind]}</span>'
        f'<h3>{escape(exercise["prompt"])}</h3>{extra}'
        f'<p class="explanation">{escape(exercise["explanation"])}</p></article>'
    )


def main():
    parser = argparse.ArgumentParser(description="Export unpublished pilot content as a local review document.")
    parser.add_argument("--source", type=Path, default=Path(__file__).resolve().parents[1] / "content" / "pilot-greetings.v1.json")
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    draft = json.loads(args.source.read_text(encoding="utf-8"))
    if draft["reviewStatus"] not in ("pending-linguistic-review", "approved-by-owner"):
        raise ValueError("This exporter expects content with a known reviewStatus.")
    approved = draft["reviewStatus"] == "approved-by-owner"
    badge = "UNITÀ PILOTA APPROVATA DAL COMMITTENTE" if approved else "BOZZA EDITORIALE · NON PUBBLICATA"
    sections = []
    for index, lesson in enumerate(draft["lessons"], 1):
        sections.append(
            f'<section id="lesson-{index}"><h2>{index}. {escape(lesson["title"])}</h2>'
            f'<p><b>Obiettivo:</b> {escape(lesson["objective"])}</p>'
            f'<p class="warning"><b>Da revisionare:</b> {escape(lesson["reviewNote"])}</p>'
            + "".join(exercise_html(exercise) for exercise in lesson["exercises"])
            + "</section>"
        )
    navigation = " ".join(
        f'<a href="#lesson-{index}">{index}. {escape(lesson["title"])}</a>'
        for index, lesson in enumerate(draft["lessons"], 1)
    )
    document = """<!doctype html><html lang="it"><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Siculingo - Revisione delle cinque lezioni pilota</title>
<style>
body{font:16px/1.7 system-ui,sans-serif;color:#332447;background:#fffaf4;margin:0}
main{max-width:900px;margin:auto;padding:35px 20px}h1,h2,h3{line-height:1.3}
h1{font-size:34px}h2{font-size:28px;color:#6a00f4}h3{font-size:18px}
.warning{background:#fff0d6;padding:18px;border-left:4px solid #c17a00}
nav{display:flex;flex-wrap:wrap;gap:12px}a{color:#4a00ac}section{margin-top:50px}
.exercise{background:white;border:1px solid #e4d8ee;border-radius:14px;padding:20px;margin:18px 0;break-inside:avoid}
.exercise>span{font-size:12px;color:#766b84}.phrase{font-size:20px;font-weight:bold}
.explanation{font-size:14px;color:#655570}li{margin:5px 0}strong{color:#4a00ac}
@media print{body{background:white}nav{display:none}section{break-before:page}}
</style><main><p>""" + badge + """</p>
<h1>Cinque lezioni, quaranta piccoli passi.</h1>
<p>Documento per la revisione del committente: include volutamente le soluzioni.
Non è una pagina dell'app, un tentativo di lezione o un'approvazione linguistica.</p>
"""
    document += f'<p class="warning">{escape(draft["editorialNote"])}</p>'
    document += "<p><b>Da concordare:</b> grafia catanese di riferimento, accenti e apostrofi, varianti ammesse e registro informale. Segnalare le correzioni usando il codice dell'esercizio. Le ripetizioni sono intenzionali per riconoscimento e richiamo; la loro efficacia andrà valutata sul pilota.</p>"
    document += f"<nav>{navigation}</nav>" + "".join(sections) + "</main></html>"
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(document, encoding="utf-8")
    print(f"Review exported: {len(draft['lessons'])} lessons, {sum(len(lesson['exercises']) for lesson in draft['lessons'])} exercises.")


if __name__ == "__main__":
    main()
