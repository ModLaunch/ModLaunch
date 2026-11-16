// Генератор страниц сайта modlaunchapp.com: загрузка, лист ожидания, пресс-кит, итог оплаты, документы.
// Шапка и подвал — как у главной; русский текст — в разметке, английский — в словаре страницы (как на главной).
// Документы (docs/legal/*.md) превращаются в HTML. Запуск: node tools/site-pages.js
const fs = require('fs');
const path = require('path');
const root = path.resolve(__dirname, '..');
const site = path.join(root, 'site');
const FUNCTIONS = 'https://europe-west1-modhub-reviews.cloudfunctions.net';

const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const mark = '<span class="mark" aria-hidden="true"><svg viewBox="-1.5 -1.5 27 27"><path d="M4.3 4.3 19.7 19.7M19.7 4.3 4.3 19.7" stroke="#7a62d6" stroke-width="1.6"/><circle cx="4.3" cy="4.3" r="3" fill="#cbbcff"/><circle cx="19.7" cy="4.3" r="3" fill="#f2c25c"/><circle cx="4.3" cy="19.7" r="3" fill="#8f73ff"/><circle cx="19.7" cy="19.7" r="3" fill="#cbbcff"/><circle cx="12" cy="12" r="5.6" fill="#fff"/><circle cx="12" cy="12" r="2.6" fill="#7c5cff"/></svg></span>';

function page({ file, title, description, body, en = {}, script = '', depth = 0 }) {
  const up = '../'.repeat(depth);
  return `<!doctype html>
<html lang="ru">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>${esc(title)}</title>
    <meta name="description" content="${esc(description)}" />
    <meta name="theme-color" content="#17171a" />
    <link rel="canonical" href="https://modlaunchapp.com/${file}" />
    <meta property="og:site_name" content="ModLaunch" />
    <meta property="og:title" content="${esc(title)}" />
    <meta property="og:description" content="${esc(description)}" />
    <meta property="og:image" content="https://modlaunchapp.com/img/og.jpg" />
    <link rel="icon" href="${up}img/logo.svg" type="image/svg+xml" />
    <link rel="preconnect" href="https://fonts.googleapis.com" />
    <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
    <link href="https://fonts.googleapis.com/css2?family=Onest:wght@400;500;600;700&family=Unbounded:wght@500;600;700&family=JetBrains+Mono:wght@400;600&display=swap" rel="stylesheet" />
    <link rel="stylesheet" href="${up}style.css" />
    <link rel="stylesheet" href="${up}pages.css" />
  </head>
  <body>
    <header class="nav" id="nav">
      <div class="wrap nav__in">
        <a class="brand" href="${up}./" aria-label="ModLaunch">${mark}<span>ModLaunch</span></a>
        <nav class="nav__links" aria-label="Разделы">
          <a href="${up}./#platform" data-t="nav.platform">Платформа</a>
          <a href="${up}./#mods" data-t="nav.mods">Моды</a>
          <a href="${up}hub.html" data-t="nav.hub">Hub</a>
          <a href="${up}waitlist.html" data-t="nav.waitlist">Лист ожидания</a>
          <a href="${up}press.html" data-t="nav.press">Пресс-кит</a>
        </nav>
        <div class="nav__end">
          <button class="icon-btn" id="theme" type="button" aria-label="Тема"><svg viewBox="0 0 24 24"><path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/></svg></button>
          <button class="lang" id="lang" type="button" aria-label="Language">EN</button>
          <a class="btn btn--sm btn--primary" href="${up}download.html" data-t="nav.download">Скачать</a>
          <button class="icon-btn burger" id="burger" type="button" aria-label="Меню" aria-expanded="false"><svg viewBox="0 0 24 24"><path d="M4 7h16M4 12h16M4 17h16"/></svg></button>
        </div>
      </div>
    </header>
    <main class="page wrap">
${body}
    </main>
    <footer class="foot">
      <div class="wrap foot__in">
        <span>© 2026 ModLaunch</span>
        <a href="${up}download.html" data-t="nav.download">Скачать</a>
        <a href="${up}press.html" data-t="nav.press">Пресс-кит</a>
        <a href="${up}legal/terms.html" data-t="foot.terms">Соглашение</a>
        <a href="${up}legal/privacy.html" data-t="foot.privacy">Конфиденциальность</a>
        <a href="${up}legal/refunds.html" data-t="foot.refunds">Возврат денег</a>
        <a href="${up}legal/rightsholders.html" data-t="foot.rights">Правообладателям</a>
        <a href="mailto:support@modlaunchapp.com">support@modlaunchapp.com</a>
        <span class="foot__note" data-t="foot.note">Официальный сайт ModLaunch&nbsp;— modlaunchapp.com. ModLaunch не связан с Valve, Epic Games, GOG, Mojang, Microsoft и издателями игр; названия и обложки игр принадлежат их владельцам.</span>
      </div>
    </footer>
    <script src="${up}js/core.js"></script>
    <script>
      Object.assign(EN, ${JSON.stringify({
        'nav.waitlist': 'Waitlist', 'nav.press': 'Press kit', 'foot.terms': 'Terms', 'foot.privacy': 'Privacy',
        'foot.refunds': 'Refunds', 'foot.rights': 'Rights holders', ...en,
      })});
      applyLang();
${script}
    </script>
  </body>
</html>
`;
}

// ---------------------------------------------------------------- Markdown → HTML (для документов)

function inline(s) {
  return esc(s)
    .replace(/\*\*(.+?)\*\*/g, '<b>$1</b>')
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\[([^\]]+)\]\((https?:[^)]+)\)/g, '<a href="$2" rel="noopener">$1</a>')
    .replace(/\[([^\]]+)\]\(([A-Z-]+)\.md\)/g, (m, text, name) => `<a href="${LEGAL[name] ?? '#'}">${text}</a>`);
}

function markdown(md) {
  const out = [];
  const lines = md.replace(/\r/g, '').split('\n');
  let list = null;
  const close = () => { if (list) { out.push(`</${list}>`); list = null; } };
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (/^\s*$/.test(line)) { close(); continue; }
    let m;
    if ((m = line.match(/^(#{1,3}) (.*)$/))) { close(); out.push(`<h${m[1].length + 0}>${inline(m[2])}</h${m[1].length}>`); continue; }
    if (line.startsWith('> ')) { close(); out.push(`<p class="note">${inline(line.slice(2))}</p>`); continue; }
    if (line.startsWith('|')) {
      close();
      const rows = [];
      while (i < lines.length && lines[i].startsWith('|')) { rows.push(lines[i]); i++; }
      i--;
      const cells = (r) => r.split('|').slice(1, -1).map((c) => c.trim());
      const [head, , ...body] = rows;
      out.push('<div class="table"><table><thead><tr>' + cells(head).map((c) => `<th>${inline(c)}</th>`).join('') + '</tr></thead><tbody>'
        + body.map((r) => '<tr>' + cells(r).map((c) => `<td>${inline(c)}</td>`).join('') + '</tr>').join('') + '</tbody></table></div>');
      continue;
    }
    if ((m = line.match(/^\s*(\*|-|\d+\.) (.*)$/))) {
      const kind = /\d/.test(m[1]) ? 'ol' : 'ul';
      if (list !== kind) { close(); out.push(`<${kind}>`); list = kind; }
      out.push(`<li>${inline(m[2])}</li>`);
      continue;
    }
    // Продолжение пункта списка (строка с отступом) — в тот же пункт.
    if (list && /^\s+\S/.test(line)) { out[out.length - 1] = out[out.length - 1].replace(/<\/li>$/, ' ' + inline(line.trim()) + '</li>'); continue; }
    if (line.startsWith('```')) { close(); const code = []; i++; while (i < lines.length && !lines[i].startsWith('```')) code.push(lines[i++]); out.push(`<pre>${esc(code.join('\n'))}</pre>`); continue; }
    close();
    // Абзац из нескольких строк подряд — один абзац.
    if (out.length && /<\/p>$/.test(out[out.length - 1]) && !out[out.length - 1].startsWith('<p class') && i > 0 && !/^\s*$/.test(lines[i - 1])) {
      out[out.length - 1] = out[out.length - 1].replace(/<\/p>$/, ' ' + inline(line) + '</p>');
      continue;
    }
    out.push(`<p>${inline(line)}</p>`);
  }
  close();
  return out.join('\n');
}

const LEGAL = {
  'TERMS-OF-SERVICE': 'terms.html', 'PRIVACY-POLICY': 'privacy.html', 'REFUND-POLICY': 'refunds.html', 'DMCA-PROCEDURE': 'rightsholders.html',
  'DEVELOPER-AGREEMENT': 'developers.html', 'PAID-MODS-RULES': 'paid-mods.html',
};

// ---------------------------------------------------------------- страницы

const pages = [];

pages.push({
  file: 'download.html', title: 'Скачать ModLaunch для Windows', description: 'Установщик ModLaunch для Windows 10 и 11: бесплатно, около 160 МБ.',
  body: `      <section class="hero-mini">
        <h1 data-t="dl.title">Скачать ModLaunch</h1>
        <p class="lead" data-t="dl.lead">Для Windows 10 и 11 (64-бит). Бесплатно. Один файл — установщик и программа сразу.</p>
        <a class="btn btn--primary btn--lg js-setup" href="https://github.com/ModLaunch/ModLaunch/releases/latest">
          <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 4v11m0 0-4.5-4.5M12 15l4.5-4.5M5 19h14" /></svg>
          <span><span data-t="hero.download">Скачать для Windows</span><small class="js-meta">бесплатно</small></span>
        </a>
        <p class="small"><a class="js-zip" href="https://github.com/ModLaunch/ModLaunch/releases/latest" data-t="dl.zip">Портативная версия (zip)</a> · <a href="https://github.com/ModLaunch/ModLaunch/releases" data-t="foot.releases">Все версии</a></p>
        <p class="small mono" id="sha"></p>
      </section>
      <section class="grid2">
        <div class="box"><h2 data-t="dl.steps">Как установить</h2><ol>
          <li data-t="dl.s1">Запустите скачанный файл ModLaunch-Setup.</li>
          <li data-t="dl.s2">Если Windows покажет «Windows защитила ваш компьютер» — нажмите «Подробнее» → «Выполнить в любом случае». Так бывает с новыми программами, пока у них мало скачиваний.</li>
          <li data-t="dl.s3">Выберите папку (по умолчанию — ваша папка программ, права администратора не нужны) и нажмите «Установить».</li>
          <li data-t="dl.s4">ModLaunch сам найдёт ваши игры из Steam, Epic, GOG, Xbox, EA, Ubisoft и Battle.net.</li>
        </ol></div>
        <div class="box"><h2 data-t="dl.req">Требования</h2><ul>
          <li data-t="dl.r1">Windows 10 или 11, 64-бит</li>
          <li data-t="dl.r2">~400 МБ памяти во время работы, ~200 МБ на диске</li>
          <li data-t="dl.r3">Интернет — для каталога модов; библиотека и запуск игр работают и без него</li>
        </ul>
        <h2 data-t="dl.safe">Безопасность</h2>
        <p data-t="dl.safeText">Сверьте контрольную сумму SHA-256 файла с указанной выше. Обновления программа проверяет сама (контрольная сумма и подпись).</p></div>
      </section>`,
  en: {
    'dl.title': 'Download ModLaunch', 'dl.lead': 'For Windows 10 and 11 (64-bit). Free. One file — installer and app in one.', 'dl.zip': 'Portable version (zip)',
    'dl.steps': 'How to install', 'dl.s1': 'Run the downloaded ModLaunch-Setup file.',
    'dl.s2': 'If Windows shows “Windows protected your PC”, click “More info” → “Run anyway”. This happens with new apps until they have enough downloads.',
    'dl.s3': 'Pick a folder (by default your programs folder, no admin rights needed) and click “Install”.',
    'dl.s4': 'ModLaunch finds your games from Steam, Epic, GOG, Xbox, EA, Ubisoft and Battle.net by itself.',
    'dl.req': 'Requirements', 'dl.r1': 'Windows 10 or 11, 64-bit', 'dl.r2': '~400 MB RAM while running, ~200 MB of disk', 'dl.r3': 'Internet for the mod catalog; your library and games work offline too',
    'dl.safe': 'Security', 'dl.safeText': 'Compare the file’s SHA-256 with the one shown above. The app checks its own updates (checksum and signature).',
  },
  script: `      fetch('update.json', { cache: 'no-cache' }).then((r) => r.ok ? r.json() : null).then((u) => {
        if (u && /^[0-9a-f]{64}$/i.test(u.sha256 || '')) document.getElementById('sha').textContent = 'SHA-256 (' + u.version + '): ' + u.sha256;
      }).catch(() => {});`,
});

pages.push({
  file: 'waitlist.html', title: 'Лист ожидания ModLaunch', description: 'Запишитесь — пришлём приглашение в день открытия магазина ModLaunch.',
  body: `      <section class="hero-mini">
        <h1 data-t="wl.title">Лист ожидания</h1>
        <p class="lead" data-t="wl.lead">Магазин игр ModLaunch откроется для всех волнами. Оставьте почту — пришлём приглашение в первой волне.</p>
        <p class="ok" id="ok" hidden data-t="wl.confirmed">Почта подтверждена — вы в листе ожидания. Приглашение придёт на неё.</p>
        <p class="bad" id="badlink" hidden data-t="wl.badLink">Ссылка устарела или неверна. Запишитесь ещё раз.</p>
        <form class="wl" id="wl" novalidate>
          <input type="email" id="email" required maxlength="254" autocomplete="email" data-tp="wl.email" placeholder="ваша@почта.ru" />
          <button class="btn btn--primary" type="submit" data-t="wl.send">Записаться</button>
        </form>
        <label class="small consent"><input type="checkbox" id="agree" /> <span data-t="wl.agree">Согласен(на) на обработку почты для приглашения (<a href="legal/privacy.html">политика</a>). Отписаться можно в любой момент.</span></label>
        <p class="small" id="status" role="status"></p>
      </section>`,
  en: {
    'wl.title': 'Waitlist', 'wl.lead': 'The ModLaunch game store opens in waves. Leave your e-mail and we’ll send an invite in the first wave.',
    'wl.confirmed': 'E-mail confirmed — you’re on the waitlist. The invite will come to this address.', 'wl.badLink': 'The link is outdated or invalid. Please sign up again.',
    'wl.email': 'your@email.com', 'wl.send': 'Join', 'wl.agree': 'I agree to the processing of my e-mail for the invite (<a href="legal/privacy.html">policy</a>). Unsubscribe any time.',
    'wl.sent': 'Almost done! Check your inbox and click the confirmation link.', 'wl.bad': 'Check the e-mail address.', 'wl.need': 'Please tick the consent box.', 'wl.fail': 'Couldn’t sign up right now — try again later.',
  },
  script: `      const q = new URLSearchParams(location.search);
      if (q.get('ok')) document.getElementById('ok').hidden = false;
      if (q.get('bad')) document.getElementById('badlink').hidden = false;
      const say = (k, ruText) => { document.getElementById('status').textContent = lang === 'en' ? EN[k] : ruText; };
      document.getElementById('wl').addEventListener('submit', async (e) => {
        e.preventDefault();
        const email = document.getElementById('email').value.trim();
        if (!/^[^\\s@]+@[^\\s@]+\\.[^\\s@]{2,}$/.test(email)) return say('wl.bad', 'Проверьте адрес почты.');
        if (!document.getElementById('agree').checked) return say('wl.need', 'Отметьте согласие.');
        try {
          const r = await fetch('${FUNCTIONS}/waitlistSignup', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email, lang, source: q.get('src') || 'site' }) });
          if (!r.ok) throw new Error(r.status);
          say('wl.sent', 'Почти готово! Проверьте почту и нажмите ссылку в письме.');
        } catch { say('wl.fail', 'Сейчас записаться не получилось — попробуйте позже.'); }
      });`,
});

pages.push({
  file: 'press.html', title: 'ModLaunch — пресс-кит', description: 'Логотипы, снимки экрана, описание и факты о ModLaunch для прессы и блогеров.',
  body: `      <section class="hero-mini">
        <h1 data-t="pr.title">Пресс-кит</h1>
        <p class="lead" data-t="pr.lead">Всё для статьи или видео о ModLaunch. Можно использовать без запроса, ссылка на modlaunchapp.com — приветствуется.</p>
        <a class="btn btn--primary" href="press/modlaunch-presskit.zip" data-t="pr.zip">Скачать всё одним архивом</a>
      </section>
      <section class="grid2">
        <div class="box"><h2 data-t="pr.facts">Факты</h2><div class="table"><table><tbody>
          <tr><th data-t="pr.f.name">Название</th><td>ModLaunch</td></tr>
          <tr><th data-t="pr.f.what">Что это</th><td data-t="pr.f.whatV">Платформа для игр и модов на Windows: одна библиотека для всех лаунчеров, 100 000+ модов в один клик, магазин инди-игр, друзья, студия модов</td></tr>
          <tr><th data-t="pr.f.dev">Разработчик</th><td data-t="pr.f.devV">ООО «____», Минск, Беларусь</td></tr>
          <tr><th data-t="pr.f.platform">Платформа</th><td>Windows 10 / 11</td></tr>
          <tr><th data-t="pr.f.price">Цена</th><td data-t="pr.f.priceV">Бесплатно; магазин игр и подписка Premium — по желанию</td></tr>
          <tr><th data-t="pr.f.langs">Языки</th><td>Русский, English, Українська, Español, Deutsch, Français, Português (BR), 中文</td></tr>
          <tr><th data-t="pr.f.site">Сайт</th><td><a href="https://modlaunchapp.com">modlaunchapp.com</a></td></tr>
          <tr><th data-t="pr.f.contact">Для прессы</th><td><a href="mailto:press@modlaunchapp.com">press@modlaunchapp.com</a></td></tr>
        </tbody></table></div></div>
        <div class="box"><h2 data-t="pr.short">Коротко</h2>
          <p data-t="pr.shortV">ModLaunch собирает игры из Steam, Epic, GOG, Xbox и других лаунчеров в одну библиотеку и ставит моды в один клик — с откатом, если что-то сломалось. Внутри — магазин инди-игр, друзья и чат, облачные сохранения и студия для создания модов.</p>
          <h2 data-t="pr.long">Подробнее</h2>
          <p data-t="pr.longV">ModLaunch находит установленные игры сам, показывает моды из Thunderstore, Nexus Mods и Modrinth, ставит их вместе с зависимостями и загрузчиками, хранит профили сборок и умеет откатить мод к прошлой версии. «Доктор вылетов» объясняет, что сломалось, и предлагает исправление. Независимые разработчики продают в ModLaunch свои игры по региональным ценам с комиссией площадки до 29 %; авторы модов получают поддержку от игроков. Каждый выложенный файл проверяется на вирусы.</p>
        </div>
      </section>
      <section class="box"><h2 data-t="pr.logos">Логотипы</h2>
        <div class="tiles"><a href="img/logo.svg" download><img src="img/logo.svg" alt="ModLaunch logo (SVG)" /><span>SVG</span></a>
        <a href="press/modlaunch-icon-512.png" download><img src="press/modlaunch-icon-512.png" alt="ModLaunch icon 512" /><span>PNG 512</span></a></div>
      </section>
      <section class="box"><h2 data-t="pr.shots">Снимки экрана</h2><div class="shots" id="shots"></div></section>`,
  en: {
    'pr.title': 'Press kit', 'pr.lead': 'Everything for an article or video about ModLaunch. Free to use, a link to modlaunchapp.com is appreciated.', 'pr.zip': 'Download everything as one archive',
    'pr.facts': 'Facts', 'pr.f.name': 'Name', 'pr.f.what': 'What it is', 'pr.f.whatV': 'A games-and-mods platform for Windows: one library for every launcher, 100,000+ one-click mods, an indie game store, friends, a mod studio',
    'pr.f.dev': 'Developer', 'pr.f.devV': '“____” LLC, Minsk, Belarus', 'pr.f.platform': 'Platform', 'pr.f.price': 'Price', 'pr.f.priceV': 'Free; the game store and Premium are optional',
    'pr.f.langs': 'Languages', 'pr.f.site': 'Website', 'pr.f.contact': 'Press contact', 'pr.short': 'In short',
    'pr.shortV': 'ModLaunch gathers games from Steam, Epic, GOG, Xbox and other launchers into one library and installs mods in one click — with a rollback if something breaks. It also has an indie game store, friends and chat, cloud saves and a mod-making studio.',
    'pr.long': 'In more detail',
    'pr.longV': 'ModLaunch finds installed games by itself, shows mods from Thunderstore, Nexus Mods and Modrinth, installs them with dependencies and loaders, keeps setup profiles and can roll a mod back. The crash doctor explains what broke and offers a fix. Independent developers sell their games in ModLaunch with regional prices and a platform fee of at most 29 %; mod authors get support from players. Every uploaded file is scanned for malware.',
    'pr.logos': 'Logos', 'pr.shots': 'Screenshots',
  },
  script: `      const shots = ['home', 'library', 'game', 'game-installed', 'market', 'friends', 'bigpicture', 'create', 'earnings', 'profile'];
      const box = document.getElementById('shots');
      const render = () => { box.innerHTML = shots.map((s) => '<a href="img/app/' + (lang === 'en' ? 'en' : 'ru') + '/' + s + '.webp" target="_blank"><img loading="lazy" src="img/app/' + (lang === 'en' ? 'en' : 'ru') + '/' + s + '.webp" alt="' + s + '" /></a>').join(''); };
      langHooks.push(render); render();`,
});

for (const [kind, ru, en] of [['success', 'Оплата прошла', 'Payment complete'], ['fail', 'Оплата не прошла', 'Payment failed']]) {
  pages.push({
    file: `pay/${kind}.html`, depth: 1, title: `ModLaunch — ${ru}`, description: ru,
    body: kind === 'success'
      ? `      <section class="hero-mini"><h1 data-t="pay.ok">Оплата прошла</h1><p class="lead" data-t="pay.okText">Вернитесь в ModLaunch — покупка появится сама в течение минуты. Чек — в разделе Аккаунт → Покупки.</p><p class="small mono" id="pid"></p></section>`
      : `      <section class="hero-mini"><h1 data-t="pay.bad">Оплата не прошла</h1><p class="lead" data-t="pay.badText">Деньги не списаны. Вернитесь в ModLaunch и попробуйте ещё раз или другую карту. Если деньги всё же списались — напишите на support@modlaunchapp.com, указав номер ниже.</p><p class="small mono" id="pid"></p></section>`,
    en: kind === 'success'
      ? { 'pay.ok': en, 'pay.okText': 'Go back to ModLaunch — your purchase appears within a minute. The receipt is in Account → Purchases.' }
      : { 'pay.bad': en, 'pay.badText': 'No money was charged. Go back to ModLaunch and try again or use another card. If you were charged anyway, write to support@modlaunchapp.com with the number below.' },
    script: `      const p = new URLSearchParams(location.search).get('p');
      if (p && /^p_\\d+_[0-9a-f]{12}$/.test(p)) document.getElementById('pid').textContent = p;`,
  });
}

const draft = `<p class="note" data-t="legal.draft">Документ в редакции на согласовании. Вопросы — support@modlaunchapp.com.</p>`;
for (const [name, file] of Object.entries(LEGAL)) {
  const md = fs.readFileSync(path.join(root, 'docs', 'legal', `${name}.md`), 'utf8')
    .replace(/\r/g, '')
    // Плашка «ЧЕРНОВИК — показать юристу» (первая цитата целиком) на сайт не идёт: вместо неё — «на согласовании».
    .replace(/^> ⚠️[^\n]*(\n>[^\n]*)*\n?/m, '')
    .replace(/\*\*\[ЮРИСТ\]\*\*:\s*/g, '')
    .replace(/ ?\*\*\[ЮРИСТ\][^*]*\*\*/g, '').replace(/ ?\[ЮРИСТ\]/g, '');
  const title = (md.match(/^# (.*)$/m) || [, name])[1];
  pages.push({
    file: `legal/${file}`, depth: 1, title: `${title} — ModLaunch`, description: title,
    body: `      <article class="doc">${draft}\n${markdown(md)}</article>`,
    en: { 'legal.draft': 'This document is under review. The Russian text is authoritative. Questions: support@modlaunchapp.com.' },
  });
}

for (const p of pages) {
  const target = path.join(site, p.file);
  fs.mkdirSync(path.dirname(target), { recursive: true });
  fs.writeFileSync(target, page(p), 'utf8');
  console.log('wrote', path.relative(root, target));
}
