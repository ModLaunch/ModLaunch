'use strict';

/* ModScript в браузере — перенос компилятора из программы (Creator/ModScript.cs).
   Та же грамматика, те же ошибки и тот же результат: пакет Content Patcher для
   Stardew Valley и пакет Thunderstore для игр на BepInEx. */

const ModScript = (() => {
  const JSON_MARK = '\u0001json:';
  class ScriptError extends Error {}

  function lex(line, no, diags) {
    const toks = [];
    let i = 0;
    while (i < line.length) {
      const c = line[i];
      if (/\s/.test(c)) { i++; continue; }
      if (c === '#') break;
      if (c === '"') {
        let s = '';
        let closed = false;
        i++;
        while (i < line.length) {
          if (line[i] === '\\' && i + 1 < line.length) {
            const n = line[i + 1];
            s += n === 'n' ? '\n' : n === 't' ? '\t' : n;
            i += 2;
            continue;
          }
          if (line[i] === '"') { closed = true; i++; break; }
          s += line[i++];
        }
        if (!closed) diags.push({ line: no, msg: 'quote' });
        toks.push({ k: 's', t: s });
        continue;
      }
      if ('={}[]'.includes(c)) { toks.push({ k: 'y', t: c }); i++; continue; }
      const start = i;
      while (i < line.length && !/\s/.test(line[i]) && !'={}[]"#'.includes(line[i])) i++;
      toks.push({ k: 'w', t: line.slice(start, i) });
    }
    return toks;
  }

  function parse(src, diags) {
    const root = [];
    const stack = [{ owner: null, list: root }];
    src.replace(/\r\n/g, '\n').split('\n').forEach((text, n) => {
      let toks = lex(text, n + 1, diags);
      if (!toks.length) return;
      if (toks[0].k === 'y' && toks[0].t === '}') {
        if (stack.length === 1) diags.push({ line: n + 1, msg: 'extraBrace' });
        else stack.pop();
        toks = toks.slice(1);
        if (!toks.length) return;
      }
      const opens = toks[toks.length - 1].k === 'y' && toks[toks.length - 1].t === '{';
      const node = { line: n + 1, toks: opens ? toks.slice(0, -1) : toks, body: null };
      stack[stack.length - 1].list.push(node);
      if (opens) { node.body = []; stack.push({ owner: node, list: node.body }); }
    });
    while (stack.length > 1) diags.push({ line: stack.pop().owner.line, msg: 'openBrace' });
    return root;
  }

  function calc(expr) {
    const s = expr.replace(/\s/g, '');
    let pos = 0;
    const num = () => {
      if (s[pos] === '(') { pos++; const v = sum(); if (s[pos] === ')') pos++; return v; }
      if (s[pos] === '-') { pos++; return -num(); }
      const start = pos;
      while (pos < s.length && /[\d.]/.test(s[pos])) pos++;
      const v = parseFloat(s.slice(start, pos));
      if (Number.isNaN(v)) throw new Error('nan');
      return v;
    };
    const mul = () => {
      let v = num();
      while ('*/%'.includes(s[pos]) && pos < s.length) { const op = s[pos++]; const r = num(); v = op === '*' ? v * r : op === '/' ? v / r : v % r; }
      return v;
    };
    const sum = () => {
      let v = mul();
      while ('+-'.includes(s[pos]) && pos < s.length) { const op = s[pos++]; const r = mul(); v = op === '+' ? v + r : v - r; }
      return v;
    };
    try { const v = sum(); return pos === s.length && Number.isFinite(v) ? Math.round(v * 1e6) / 1e6 : null; } catch { return null; }
  }

  function compile(source) {
    const b = { name: '', version: '1.0.0', author: '', about: '', game: '', needs: [], configs: [], inis: [], copies: [], writes: [], changes: [], diags: [], log: [], statements: 0 };
    const vars = new Map();
    const when = [];

    const subst = (text, line) => text.replace(/\$\{(\w+)\}|\$(\w+)/g, (m, a, c) => {
      const name = (a || c).toLowerCase();
      if (vars.has(name)) return vars.get(name);
      b.diags.push({ line, msg: 'unknownVar', x: a || c });
      return m;
    });
    const value = (toks, line) => {
      if (!toks.length) { b.diags.push({ line, msg: 'noValue' }); return ''; }
      if (toks.length === 1 && toks[0].k === 's') return subst(toks[0].t, line);
      if (toks.length === 2 && toks[0].k === 'w' && toks[0].t === 'json' && toks[1].k === 's') {
        const text = subst(toks[1].t, line);
        try { JSON.parse(text); } catch { b.diags.push({ line, msg: 'json' }); }
        return JSON_MARK + text;
      }
      let expr = toks.map((x) => (x.k === 's' ? `"${x.t}"` : x.t)).join(' ');
      expr = subst(expr, line);
      if (/^[\d\s.+\-*/()%]+$/.test(expr) && /[+\-*/%]/.test(expr)) { const v = calc(expr); if (v !== null) return String(v); }
      return toks.every((x) => x.k === 's') ? toks.map((x) => subst(x.t, line)).join('') : expr;
    };
    const json = (v) => {
      if (v.startsWith(JSON_MARK)) { try { return JSON.parse(v.slice(JSON_MARK.length)); } catch { return ''; } }
      if (/^-?\d+$/.test(v)) return Number(v);
      if (/^-?\d+\.\d+$/.test(v)) return Number(v);
      if (v === 'true' || v === 'false') return v === 'true';
      return v;
    };
    const addChange = (change) => {
      if (when.length) change.When = Object.fromEntries(when);
      change.LogName = `${change.Action} ${change.Target} #${b.changes.length + 1}`;
      b.changes.push(change);
    };
    const cond = (t, line) => {
      if (!t.length) throw new ScriptError('if');
      for (let i = 0; i < t.length; i++) {
        const x = t[i];
        if (x.k === 's') continue;
        let op = ['==', '!=', '>', '<', '>=', '<=', 'contains'].includes(x.t) ? x.t : (x.t === '=' || x.t === '!') ? x.t : null;
        if (!op) continue;
        let skip = 1;
        if (['=', '!', '>', '<'].includes(op) && t[i + 1]?.k === 'y' && t[i + 1].t === '=') { op += '='; skip = 2; }
        if (op === '=' || op === '!') throw new ScriptError('if');
        const left = value(t.slice(0, i), line);
        const right = value(t.slice(i + skip), line);
        const a = parseFloat(left);
        const c = parseFloat(right);
        const num = !Number.isNaN(a) && !Number.isNaN(c) && /^-?[\d.]+$/.test(left.trim()) && /^-?[\d.]+$/.test(right.trim());
        switch (op) {
          case '==': return num ? a === c : left === right;
          case '!=': return num ? a !== c : left !== right;
          case '>': return num ? a > c : left > right;
          case '<': return num ? a < c : left < right;
          case '>=': return num ? a >= c : left >= right;
          case '<=': return num ? a <= c : left <= right;
          default: return left.toLowerCase().includes(right.toLowerCase());
        }
      }
      const v = value(t, line);
      return !['', '0', 'false', 'no'].includes(v);
    };

    function run(nodes) {
      let last = null;
      for (const node of nodes) {
        if (b.statements++ > 5000) { b.diags.push({ line: node.line, msg: 'tooMuch' }); return; }
        const head = node.toks[0].t.toLowerCase();
        try {
          if (head === 'if') {
            if (!node.body) throw new ScriptError('ifBlock');
            const ok = cond(node.toks.slice(1), node.line);
            if (ok) run(node.body);
            last = ok;
            continue;
          }
          if (head === 'else') {
            if (last === null) throw new ScriptError('else');
            if (!node.body) throw new ScriptError('ifBlock');
            if (node.toks[1]?.t === 'if') {
              const ok = last === false && cond(node.toks.slice(2), node.line);
              if (ok) run(node.body);
              last = last === true || ok;
              continue;
            }
            if (last === false) run(node.body);
            last = null;
            continue;
          }
          last = null;
          exec(node);
        } catch (e) {
          b.diags.push(e instanceof ScriptError ? { line: node.line, msg: e.message.split('|')[0], x: e.message.split('|')[1] } : { line: node.line, msg: 'generic', x: e.message });
        }
      }
    }

    function exec(n) {
      const t = n.toks;
      const line = n.line;
      const cmd = t[0].t.toLowerCase();
      const S = (i) => (i < t.length ? subst(t[i].t, line) : '');
      const eq = () => t.findIndex((x) => x.k === 'y' && x.t === '=');
      const need = (count) => { if (t.length < count) throw new ScriptError('args|' + cmd); };
      if (n.body && cmd !== 'for' && cmd !== 'when') { b.diags.push({ line, msg: 'blockHere', x: cmd }); return; }
      switch (cmd) {
        case 'mod': need(2); b.name = S(1); break;
        case 'version': need(2); b.version = S(1); break;
        case 'author': need(2); b.author = S(1); break;
        case 'about': need(2); b.about = t.slice(1).map((_, i) => S(i + 1)).join(' '); break;
        case 'game': need(2); b.game = S(1).toLowerCase(); break;
        case 'icon': case 'website': need(2); break;
        case 'needs': need(2); for (let i = 1; i < t.length; i++) if (!b.needs.includes(S(i))) b.needs.push(S(i)); break;
        case 'print': b.log.push(`${line}: ${value(t.slice(1), line)}`); break;
        case 'let': {
          if (eq() !== 2 || t[1].k !== 'w' || !/^[A-Za-z_]\w*$/.test(t[1].t)) throw new ScriptError('let');
          vars.set(t[1].t.toLowerCase(), value(t.slice(3), line));
          break;
        }
        case 'for': {
          if (!n.body) throw new ScriptError('forBlock');
          if (t.length < 4 || !/^[A-Za-z_]\w*$/.test(t[1].t)) throw new ScriptError('for');
          const name = t[1].t.toLowerCase();
          let items;
          if (t[2].t === 'in') items = t.slice(3).map((x) => subst(x.t, line));
          else if (t[2].t === 'from' && t.length === 6 && t[4].t === 'to' && /^-?\d+$/.test(S(3)) && /^-?\d+$/.test(S(5))) {
            const from = Number(S(3));
            const to = Number(S(5));
            if (Math.abs(to - from) > 1000) throw new ScriptError('forRange');
            items = [];
            for (let i = from; to >= from ? i <= to : i >= to; i += to >= from ? 1 : -1) items.push(String(i));
          } else throw new ScriptError('for');
          const had = vars.has(name);
          const old = vars.get(name);
          for (const item of items) { vars.set(name, item); run(n.body); }
          if (had) vars.set(name, old); else vars.delete(name);
          break;
        }
        case 'when': {
          if (!n.body) throw new ScriptError('whenBlock');
          if (eq() !== 2) throw new ScriptError('when');
          when.push([S(1), value(t.slice(3), line)]);
          run(n.body);
          when.pop();
          break;
        }
        case 'edit': {
          if (eq() !== 4) throw new ScriptError('edit');
          addChange({ Action: 'EditData', Target: S(1), Fields: { [S(2)]: { [subst(t[3].t, line)]: json(value(t.slice(5), line)) } } });
          break;
        }
        case 'entry': case 'dialogue': case 'mail': {
          const at = eq();
          if (at !== (cmd === 'mail' ? 2 : 3)) throw new ScriptError(cmd);
          const target = cmd === 'dialogue' ? `Characters/Dialogue/${S(1)}` : cmd === 'mail' ? 'Data/mail' : S(1);
          const key = cmd === 'mail' ? S(1) : S(2);
          addChange({ Action: 'EditData', Target: target, Entries: { [key]: json(value(t.slice(at + 1), line)) } });
          break;
        }
        case 'image': {
          if (t.length !== 4 || t[2].t !== 'from') throw new ScriptError('image');
          const file = S(3).replace(/\\/g, '/');
          const name = file.split('/').pop();
          b.copies.push({ from: file, to: 'assets/' + name });
          addChange({ Action: 'Load', Target: S(1), FromFile: 'assets/' + name });
          break;
        }
        case 'config': case 'ini': {
          if (t.length < 7 || !(t[2].k === 'y' && t[2].t === '[') || !(t[4].k === 'y' && t[4].t === ']') || eq() !== 6) throw new ScriptError(cmd);
          const file = S(1).replace(/\\/g, '/');
          if (file.includes('..') || /^([a-z]:|\/)/i.test(file)) throw new ScriptError('path');
          (cmd === 'config' ? b.configs : b.inis).push({ file, section: S(3), key: subst(t[5].t, line), value: value(t.slice(7), line) });
          break;
        }
        case 'write': {
          if (eq() !== 2) throw new ScriptError('write');
          const path = S(1).replace(/\\/g, '/');
          if (path.includes('..') || /^([a-z]:|\/)/i.test(path)) throw new ScriptError('path');
          const text = value(t.slice(3), line);
          b.writes.push({ path, text: text.startsWith(JSON_MARK) ? text.slice(JSON_MARK.length) : text });
          break;
        }
        case 'copy': {
          if (t.length !== 4 || t[2].t !== 'to') throw new ScriptError('copy');
          b.copies.push({ from: S(1), to: S(3) });
          break;
        }
        default:
          b.diags.push({ line, msg: 'unknown', x: t[0].t });
      }
    }

    run(parse(source, b.diags));
    if (!b.name) b.diags.push({ line: 1, msg: 'noName' });
    if (!b.game) b.diags.push({ line: 1, msg: 'noGame' });
    if (!/^\d+\.\d+\.\d+$/.test(b.version)) b.diags.push({ line: 1, msg: 'version' });
    const seen = new Set();
    b.diags = b.diags.filter((d) => { const k = `${d.line}|${d.msg}|${d.x || ''}`; if (seen.has(k)) return false; seen.add(k); return true; }).sort((x, y) => x.line - y.line);
    b.ok = b.diags.length === 0;
    return b;
  }

  const pkgName = (s) => (s.trim().replace(/[^A-Za-z0-9_]+/g, '_').replace(/^_+|_+$/g, '') || 'MyMod').slice(0, 60);

  /** Что получится: файлы пакета, как их собирает программа. */
  function pack(b, loader) {
    const files = {};
    const pretty = (o) => JSON.stringify(o, null, 2);
    const cfg = (list) => {
      const bySection = new Map();
      list.forEach((c) => { if (!bySection.has(c.section)) bySection.set(c.section, []); bySection.get(c.section).push(c); });
      return [...bySection].map(([s, items]) => `[${s}]\n` + items.map((c) => `${c.key} = ${c.value}`).join('\n')).join('\n\n') + '\n';
    };
    if (loader === 'SMAPI') {
      const folder = `[CP] ${b.name}/`;
      files[folder + 'manifest.json'] = pretty({
        Name: b.name, Author: b.author || 'ModLaunch', Version: b.version, Description: b.about,
        UniqueID: `${pkgName(b.author || 'ModLaunch')}.${pkgName(b.name)}`, UpdateKeys: [],
        ContentPackFor: { UniqueID: 'Pathoschild.ContentPatcher' },
        Dependencies: b.needs.map((n) => ({ UniqueID: n, IsRequired: true })),
      });
      files[folder + 'content.json'] = pretty({ Format: '2.0.0', Changes: b.changes });
      b.writes.forEach((w) => (files[folder + w.path] = w.text));
    } else {
      files['manifest.json'] = pretty({
        name: pkgName(b.name), version_number: b.version, website_url: '', description: (b.about || b.name).slice(0, 250),
        dependencies: ['BepInExPack', ...b.needs].map((n) => `${n}-…`),
      });
      files['README.md'] = `# ${b.name}\n\n${b.about}\n\n_Made with ModLaunch Creator Hub (ModScript)._\n`;
      const groups = new Map();
      b.configs.forEach((c) => { if (!groups.has(c.file)) groups.set(c.file, []); groups.get(c.file).push(c); });
      groups.forEach((list, file) => (files['config/' + file] = cfg(list)));
      b.writes.forEach((w) => (files[/^(plugins|config|patchers)\//.test(w.path) ? w.path : 'plugins/' + w.path] = w.text));
    }
    if (b.inis.length) files['modlaunch-ini.json'] = pretty(b.inis);
    return files;
  }

  return { compile, pack };
})();
