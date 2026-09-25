#!/usr/bin/env python3
"""Примочки и справка ModScript для сайта — из исходников программы (Templates.cs и strings5.json).
Запуск: python3 build/site-examples.py → site/js/examples.js"""
import json, re, textwrap, pathlib
root = pathlib.Path(__file__).resolve().parent.parent
src = (root / 'desktop/ModLaunch/Creator/Templates.cs').read_text(encoding='utf-8')
s5 = json.loads((root / 'desktop/ModLaunch/Assets/strings5.json').read_text(encoding='utf-8'))
examples = []
for m in re.finditer(r'new\("([\w-]+)", "([\w-]+)", """\n(.*?)\n\s*"""\)', src, re.S):
    id_, game, body = m.groups()
    code = textwrap.dedent(body)
    examples.append({'id': id_, 'game': game, 'code': code,
                     'title': {'ru': s5['ru'].get(f'cr.ex.{id_}', id_), 'en': s5['en'].get(f'cr.ex.{id_}', id_)},
                     'text': {'ru': s5['ru'].get(f'cr.ex.{id_}.text', ''), 'en': s5['en'].get(f'cr.ex.{id_}.text', '')}})
groups = [('cr.docs.basics', ['mod', 'version', 'author', 'about', 'game', 'icon', 'needs']),
          ('cr.docs.logic', ['let', 'if', 'else', 'for', 'when', 'print']),
          ('cr.docs.stardew', ['edit', 'entry', 'dialogue', 'mail', 'image']),
          ('cr.docs.bepinex', ['config', 'ini', 'copy', 'write', 'json'])]
docs = [{'title': {l: s5[l][g] for l in ('ru', 'en')},
         'items': [{'cmd': c, 'syntax': {l: s5[l][f'cr.doc.{c}.syntax'] for l in ('ru', 'en')}, 'text': {l: s5[l][f'cr.doc.{c}'] for l in ('ru', 'en')}} for c in cmds]}
        for g, cmds in groups]
errors = {l: {k[len('cr.err.'):]: v for k, v in s5[l].items() if k.startswith('cr.err.')} for l in ('ru', 'en')}
out = '/* Создано build/site-examples.py из исходников ModLaunch — не править руками. */\n'
out += 'const MS_EXAMPLES = ' + json.dumps(examples, ensure_ascii=False, indent=1) + ';\n'
out += 'const MS_DOCS = ' + json.dumps(docs, ensure_ascii=False, indent=1) + ';\n'
out += 'const MS_ERRORS = ' + json.dumps(errors, ensure_ascii=False, indent=1) + ';\n'
(root / 'site/js/examples.js').write_text(out, encoding='utf-8')
print(len(examples), 'examples,', sum(len(d['items']) for d in docs), 'commands')
