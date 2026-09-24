'use strict';

/**
 * Друзья на настоящих правилах базы — в эмуляторе Firebase (2.1).
 *
 *   node tools/friends-check.js
 *
 * Сам поднимает эмуляторы Auth и Firestore (нужны firebase-tools и Java 21),
 * ставит правила из firebase/friends.rules и проходит весь путь: код друга,
 * запрос, «принять», «в игре», невидимка, «убрать из друзей», удаление
 * аккаунта. И проверяет то, чего правила пускать не должны: чужие запросы,
 * подделку отправителя, чтение чужих дружб, запись в чужой профиль.
 */

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

const ROOT = path.join(__dirname, '..');

if (!process.env.FIRESTORE_EMULATOR_HOST) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'modhub-firebase-'));
  const rules = fs.readFileSync(path.join(ROOT, 'firebase', 'friends.rules'), 'utf8');
  fs.writeFileSync(
    path.join(dir, 'firestore.rules'),
    `rules_version = '2';\nservice cloud.firestore {\n  match /databases/{database}/documents {\n${rules}\n  }\n}\n`
  );
  fs.writeFileSync(
    path.join(dir, 'firebase.json'),
    JSON.stringify({
      firestore: { rules: 'firestore.rules' },
      emulators: { auth: { port: 9099 }, firestore: { port: 8080 }, ui: { enabled: false }, singleProjectMode: true },
    })
  );
  const bin = process.env.FIREBASE_BIN || 'firebase';
  const result = spawnSync(
    bin,
    ['emulators:exec', '--project', 'demo-modhub', '--config', path.join(dir, 'firebase.json'), '--only', 'auth,firestore', `node "${__filename}"`],
    { stdio: 'inherit', shell: process.platform === 'win32', cwd: dir }
  );
  process.exit(result.status ?? 1);
}

const { Account } = require('../src/main/core/account');
const { FriendsClient, toFields, pairId, CODE_RE } = require('../src/main/core/friends');

const config = { projectId: 'demo-modhub', apiKey: 'fake-api-key' };
const endpoints = {
  identity: `http://${process.env.FIREBASE_AUTH_EMULATOR_HOST}/identitytoolkit.googleapis.com/v1`,
  secureToken: `http://${process.env.FIREBASE_AUTH_EMULATOR_HOST}/securetoken.googleapis.com/v1`,
  firestore: `http://${process.env.FIRESTORE_EMULATOR_HOST}/v1`,
};

let passed = 0;
let failed = 0;
function check(name, ok, detail = '') {
  if (ok) passed += 1;
  else failed += 1;
  console.log(`${ok ? 'PASS' : 'FAIL'}  друзья: ${name}${detail ? `  —  ${detail}` : ''}`);
}

function person() {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'modhub-person-'));
  const account = new Account({ config, file: path.join(dir, 'account.json'), endpoints });
  const friends = new FriendsClient({ config, account, dataDir: dir, version: '2.1.0', endpoint: endpoints.firestore });
  return { dir, account, friends };
}

/** То, что правила должны отклонить: ждём DENIED (или другой отказ). */
async function denied(name, fn) {
  try {
    await fn();
    check(name, false, 'сервер пропустил');
  } catch (error) {
    check(name, error.code === 'DENIED', error.code);
  }
}

function find(list, uid) {
  return (list ?? []).find((f) => f.uid === uid) ?? null;
}

async function main() {
  const anna = person();
  const boris = person();
  const clara = person();
  const dima = person();

  await anna.account.signUp({ name: 'Анна', email: 'anna@example.com', password: 'password-anna' });
  // Борис сначала был безымянным (оставлял отзывы), потом завёл аккаунт:
  // почта привязывается к той же записи — друзья у него тоже должны быть.
  const borisAnon = await boris.account.token();
  await boris.account.signUp({ name: 'Борис', email: 'boris@example.com', password: 'password-boris' });
  check('аккаунт после безымянной записи — тот же uid', boris.account.uid() === borisAnon.uid);
  await clara.account.signUp({ name: 'Клара', email: 'clara@example.com', password: 'password-clara' });
  const dimaAnon = await dima.account.token(); // только безымянная запись

  const annaUid = anna.account.uid();
  const borisUid = boris.account.uid();
  const claraUid = clara.account.uid();

  /* --- код друга --- */
  const annaCode = await anna.friends.myCode();
  check('у Анны есть код друга', /^[A-HJ-NP-Z2-9]{4}-[A-HJ-NP-Z2-9]{4}$/.test(annaCode ?? ''), annaCode);
  check('код тот же при повторном вопросе', (await anna.friends.myCode()) === annaCode);

  // Тот же аккаунт на другом компьютере: код находится на сервере, а не новый.
  const annaLaptop = person();
  await annaLaptop.account.signIn({ email: 'anna@example.com', password: 'password-anna' });
  check('на другом компьютере — тот же код', (await annaLaptop.friends.myCode()) === annaCode);

  const borisCode = await boris.friends.myCode();
  const claraCode = await clara.friends.myCode();

  /* --- запрос и «принять» --- */
  const sent = await boris.friends.add(annaCode.toLowerCase().replace('-', ' '));
  check('Борис отправил запрос по коду (регистр и пробел не мешают)', sent.status === 'sent' && sent.name === 'Анна', JSON.stringify(sent));
  const again = await boris.friends.add(annaCode);
  check('повторный запрос — «уже ждёт»', again.status === 'pending', again.status);

  let annaView = await anna.friends.refresh({ force: true });
  check('Анна видит входящий запрос от Бориса', find(annaView.incoming, borisUid)?.name === 'Борис', JSON.stringify(annaView.incoming));
  const borisView = await boris.friends.refresh({ force: true });
  check('Борис видит свой запрос в ожидании', Boolean(find(borisView.outgoing, annaUid)));

  const borisMe = await boris.friends.auth();
  await denied('Борис не может сам принять свой запрос', () =>
    boris.friends.commit(
      [
        {
          update: { name: boris.friends.docName(`friendships/${pairId(annaUid, borisUid)}`), fields: toFields({ status: 'accepted', toName: 'Борис' }) },
          updateMask: { fieldPaths: ['status', 'toName'] },
          updateTransforms: [{ fieldPath: 'updated', setToServerValue: 'REQUEST_TIME' }],
        },
      ],
      borisMe
    )
  );

  const claraMe = await clara.friends.auth();
  await denied('Клара не читает чужую дружбу', () =>
    clara.friends.request(clara.friends.url(`/friendships/${pairId(annaUid, borisUid)}`), { token: claraMe.idToken })
  );
  await denied('Клара не может выбрать дружбы Анны запросом', () =>
    clara.friends.request(clara.friends.url(':runQuery'), {
      method: 'POST',
      token: claraMe.idToken,
      body: {
        structuredQuery: {
          from: [{ collectionId: 'friendships' }],
          where: { fieldFilter: { field: { fieldPath: 'users' }, op: 'ARRAY_CONTAINS', value: { stringValue: annaUid } } },
        },
      },
    })
  );
  await denied('Клара не подделает запрос от имени Анны', () =>
    clara.friends.commit(
      [
        {
          update: {
            name: clara.friends.docName(`friendships/${pairId(annaUid, claraUid)}`),
            fields: toFields({ users: [annaUid, claraUid].sort(), from: annaUid, to: claraUid, fromName: 'Анна', toName: 'Клара', status: 'accepted' }),
          },
          updateTransforms: [
            { fieldPath: 'created', setToServerValue: 'REQUEST_TIME' },
            { fieldPath: 'updated', setToServerValue: 'REQUEST_TIME' },
          ],
          currentDocument: { exists: false },
        },
      ],
      claraMe
    )
  );
  await denied('Клара не пишет в профиль Анны', () =>
    clara.friends.commit(
      [
        {
          update: {
            name: clara.friends.docName(`profiles/${annaUid}`),
            fields: toFields({ name: 'Анна', code: '', state: 'offline', game: '', gameName: '', app: 'x' }),
          },
          updateMask: { fieldPaths: ['name', 'code', 'state', 'game', 'gameName', 'app'] },
          updateTransforms: [{ fieldPath: 'seen', setToServerValue: 'REQUEST_TIME' }],
        },
      ],
      claraMe
    )
  );
  await denied('лишнее поле в своём профиле не пройдёт', () =>
    clara.friends.commit(
      [
        {
          update: {
            name: clara.friends.docName(`profiles/${claraUid}`),
            fields: toFields({ name: 'Клара', code: '', state: 'online', game: '', gameName: '', app: 'x', admin: true }),
          },
          updateTransforms: [
            { fieldPath: 'seen', setToServerValue: 'REQUEST_TIME' },
            { fieldPath: 'since', setToServerValue: 'REQUEST_TIME' },
          ],
        },
      ],
      claraMe
    )
  );
  await denied('чужой код на своё имя не заведёшь', () =>
    clara.friends.commit(
      [
        {
          update: { name: clara.friends.docName('codes/ZZZZ2222'), fields: toFields({ uid: annaUid, name: 'Анна' }) },
          updateTransforms: [{ fieldPath: 'created', setToServerValue: 'REQUEST_TIME' }],
          currentDocument: { exists: false },
        },
      ],
      claraMe
    )
  );
  await denied('безымянная запись не видит коды друзей', () =>
    dima.friends.request(dima.friends.url(`/codes/${annaCode.replace('-', '')}`), { token: dimaAnon.idToken })
  );
  let dimaError = null;
  try {
    await dima.friends.refresh({ force: true });
  } catch (error) {
    dimaError = error.code;
  }
  check('без аккаунта с почтой ModHub просит войти', dimaError === 'SIGN_IN', dimaError);

  annaView = await anna.friends.accept(borisUid);
  check('Анна приняла запрос — Борис у неё в друзьях', Boolean(find(annaView.friends, borisUid)) && !annaView.incoming.length);

  /* --- «в сети», «в игре», невидимка --- */
  anna.friends.setActivity({ state: 'playing', game: 'subnautica', gameName: 'Subnautica' });
  await anna.friends.beat();
  let seen = find((await boris.friends.refresh({ force: true })).friends, annaUid);
  check('Борис видит: Анна в игре Subnautica', seen?.state === 'playing' && seen?.gameName === 'Subnautica' && Boolean(seen?.since), JSON.stringify(seen));

  anna.friends.setMode('online');
  await anna.friends.beat();
  seen = find((await boris.friends.refresh({ force: true })).friends, annaUid);
  check('«показывать только "в сети"» — без названия игры', seen?.state === 'online' && !seen?.game, JSON.stringify(seen));

  anna.friends.setMode('hidden');
  await anna.friends.beat();
  seen = find((await boris.friends.refresh({ force: true })).friends, annaUid);
  check('невидимка — «не в сети»', seen?.state === 'offline', JSON.stringify(seen));

  anna.friends.setMode('all');
  anna.friends.setActivity({ state: 'online' });
  await anna.friends.beat();
  await anna.friends.goOffline();
  seen = find((await boris.friends.refresh({ force: true })).friends, annaUid);
  check('закрыла ModHub — сразу «не в сети»', seen?.state === 'offline', JSON.stringify(seen));

  await denied('«с какого времени» из будущего не пройдёт', () =>
    anna.friends.auth().then((me) =>
      anna.friends.commit(
        [
          {
            update: {
              name: anna.friends.docName(`profiles/${annaUid}`),
              fields: { ...toFields({ name: 'Анна', code: '', state: 'online', game: '', gameName: '', app: 'x' }), since: { timestampValue: '2999-01-01T00:00:00Z' } },
            },
            updateTransforms: [{ fieldPath: 'seen', setToServerValue: 'REQUEST_TIME' }],
          },
        ],
        me
      )
    )
  );

  /* --- встречные запросы, «убрать», удаление аккаунта --- */
  const claraSent = await clara.friends.add(annaCode);
  const annaBack = await anna.friends.add(claraCode);
  check('встречный запрос сразу делает друзьями', claraSent.status === 'sent' && annaBack.status === 'accepted', `${claraSent.status}/${annaBack.status}`);
  check('Клара видит Анну в друзьях', Boolean(find((await clara.friends.refresh({ force: true })).friends, annaUid)));

  const self = await anna.friends.add(annaCode).catch((error) => error.code);
  check('свой код добавить нельзя', self === 'SELF', String(self));
  const nobody = await anna.friends.add('ABCD-EFGH').catch((error) => error.code);
  check('несуществующий код — понятная ошибка', nobody === 'NO_SUCH_CODE', String(nobody));
  const garbage = await anna.friends.add('123').catch((error) => error.code);
  check('не код вовсе — понятная ошибка', garbage === 'BAD_CODE', String(garbage));

  await anna.friends.remove(borisUid);
  check('Анна убрала Бориса — у обоих его больше нет', !find((await anna.friends.refresh({ force: true })).friends, borisUid) && !find((await boris.friends.refresh({ force: true })).friends, annaUid));

  await clara.friends.forget();
  const annaAfter = await anna.friends.refresh({ force: true });
  check('удаление аккаунта Клары убирает её из друзей', !find(annaAfter.friends, claraUid));
  const claraCodeLeft = await anna.friends.add(claraCode).catch((error) => error.code);
  check('и её код больше не находится', claraCodeLeft === 'NO_SUCH_CODE', String(claraCodeLeft));
  check('коды друзей разные', new Set([annaCode, borisCode, claraCode]).size === 3 && CODE_RE.test(borisCode.replace('-', '')));

  console.log(`\nИтого: ${passed} PASS, ${failed} FAIL`);
  process.exit(failed ? 1 : 0);
}

main().catch((error) => {
  console.error('FAIL  друзья: проверка упала —', error);
  process.exit(1);
});
