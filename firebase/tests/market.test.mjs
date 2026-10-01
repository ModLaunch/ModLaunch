import { initializeTestEnvironment, assertSucceeds, assertFails } from '@firebase/rules-unit-testing';
import { doc, collection, setDoc, getDoc, deleteDoc, updateDoc, writeBatch, increment, serverTimestamp, Timestamp } from 'firebase/firestore';
import fs from 'fs';

// Проверка правил рынка (firebase/market.rules) на эмуляторе Firestore: npm test.
// Каждая попытка обмануть экономику должна быть отклонена, честные операции — проходить.

const env = await initializeTestEnvironment({ projectId: 'demo-mk', firestore: { rules: fs.readFileSync(new URL('./all.rules', import.meta.url), 'utf8'), host: '127.0.0.1', port: 8181 } });
let pass = 0, fail = 0;
async function ok(name, p) { try { await assertSucceeds(p); pass++; console.log('ok   ' + name); } catch (e) { fail++; console.log('FAIL ' + name + ' :: ' + e.message.split('\n')[0]); } }
async function no(name, p) { try { await assertFails(p); pass++; console.log('ok   ' + name + ' (denied)'); } catch (e) { fail++; console.log('FAIL ' + name + ' — должно быть запрещено'); } }
const db = uid => env.authenticatedContext(uid, { email: uid + '@x.io' }).firestore();
const anon = uid => env.authenticatedContext(uid, {}).firestore();
const admin = async fn => env.withSecurityRulesDisabled(c => fn(c.firestore()));
const now = serverTimestamp;
const opId = d => doc(collection(d, 'ops')).id;
const wallet = (d, uid) => doc(d, 'wallets', uid);
const bal = async uid => { let v; await admin(async d => { v = (await getDoc(doc(d, 'wallets', uid))).data().balance; }); return v; };
function assetDoc(uid, extra) {
  return Object.assign({ uid, author: uid.toUpperCase(), name: 'Меч', summary: '', description: '', kind: 'model', games: ['valheim'], engine: 'unity', tags: [], images: [],
    fileName: 'sword.bundle', size: 10, sha256: '', chunks: 1, preview: '', price: 30, supply: 0, sold: 0, owners: 0, listed: true, version: '1.0.0', created: now(), updated: now() }, extra || {});
}
const newWallet = (d, uid) => setDoc(wallet(d, uid), { balance: 100, bonusAt: now(), lastOp: '', created: now() });

await env.clearFirestore();
const A = 'Alice', B = 'Bob', C = 'Carol';
const a = db(A), b = db(B), c = db(C);

// ---------- кошельки
await no('кошелёк с 1000', setDoc(wallet(a, A), { balance: 1000, bonusAt: now(), lastOp: '', created: now() }));
await ok('кошелёк A', newWallet(a, A));
await ok('кошелёк B', newWallet(b, B));
await ok('кошелёк C', newWallet(c, C));
await no('чужой кошелёк читать', getDoc(wallet(b, A)));
await no('бонус сразу', updateDoc(wallet(a, A), { balance: increment(25), bonusAt: now() }));
await admin(d => updateDoc(doc(d, 'wallets', A), { bonusAt: Timestamp.fromMillis(Date.now() - 21 * 3600e3) }));
{ const w = writeBatch(a); const id = opId(a);
  w.update(wallet(a, A), { balance: increment(25), bonusAt: now() });
  w.set(doc(a, 'ops', id), { kind: 'bonus', from: 'bonus', to: A, amount: 25, asset: '', ref: '', by: A, at: now() });
  await ok('бонус через 21 ч', w.commit()); }
await no('бонус +50', (async () => { await admin(d => updateDoc(doc(d, 'wallets', A), { bonusAt: Timestamp.fromMillis(Date.now() - 21 * 3600e3) })); return updateDoc(wallet(a, A), { balance: increment(50), bonusAt: now() }); })());
await admin(d => updateDoc(doc(d, 'wallets', A), { balance: 100 }));
{ const w = writeBatch(a); const id = opId(a);
  w.set(doc(a, 'ops', id), { kind: 'bonus', from: B, to: A, amount: 50, asset: '', ref: '', by: A, at: now() });
  w.update(wallet(a, A), { balance: increment(50), lastOp: id });
  await no('деньги из воздуха (bonus-операция в журнале)', w.commit()); }
{ const w = writeBatch(a); const id = opId(a);
  w.set(doc(a, 'ops', id), { kind: 'buy', from: A, to: A, amount: 50, asset: 'x', ref: '', by: A, at: now() });
  w.update(wallet(a, A), { balance: increment(50), lastOp: id });
  await no('перевод самому себе', w.commit()); }
{ const w = writeBatch(a); const id = opId(a);
  w.set(doc(a, 'ops', id), { kind: 'resale', from: B, to: A, amount: 50, asset: 'x', ref: '', by: A, at: now() });
  w.update(wallet(a, A), { balance: increment(50), lastOp: id });
  w.update(wallet(a, B), { balance: increment(-50), lastOp: id });
  await no('списать с чужого кошелька', w.commit()); }

// ---------- ассеты и покупка
const S = 'bob_sword';
await no('ассет с чужим префиксом', setDoc(doc(a, 'assets', S), assetDoc(A)));
await ok('ассет B: меч за 30', setDoc(doc(b, 'assets', S), assetDoc(B)));
await ok('кусок файла B', setDoc(doc(b, 'vault', S + '_0'), { uid: B, asset: S, n: 0, data: 'AAAA' }));
await no('чужой файл платного ассета', getDoc(doc(a, 'vault', S + '_0')));
function buy(d, uid, asset, seller, price, serial, opts = {}) {
  const w = writeBatch(d); const id = opId(d);
  if (!opts.noOp) w.set(doc(d, 'ops', id), { kind: 'buy', from: uid, to: seller, amount: opts.amount ?? price, asset, ref: '', by: uid, at: now() });
  if (!opts.noPay) w.update(wallet(d, uid), { balance: increment(-(opts.amount ?? price)), lastOp: id });
  if (!opts.noSeller) w.update(wallet(d, seller), { balance: increment(opts.amount ?? price), lastOp: id });
  w.set(doc(d, 'owns', uid + '_' + asset), { uid, asset, serial, via: 'buy', op: id, ref: '', prev: '', paid: price, at: now() });
  if (!opts.noCount) w.update(doc(d, 'assets', asset), { sold: increment(1), owners: increment(1) });
  return w.commit();
}
await no('купить дешевле', buy(a, A, S, B, 30, 1, { amount: 10 }));
await no('купить, не заплатив продавцу', buy(a, A, S, B, 30, 1, { noSeller: true }));
await no('копия без операции', buy(a, A, S, B, 30, 1, { noOp: true, noPay: true, noSeller: true }));
await no('копия без счётчика', buy(a, A, S, B, 30, 0, { noCount: true }));
await ok('A покупает меч', buy(a, A, S, B, 30, 1));
console.log('     балансы', await bal(A), await bal(B));
await no('купить второй раз', buy(a, A, S, B, 30, 2));
{ let old; await admin(async d => { const { getDocs, query, where } = await import('firebase/firestore'); old = (await getDocs(query(collection(d, 'ops'), where('kind', '==', 'buy')))).docs[0].id; });
  await no('зачислить себе по чужой старой операции', updateDoc(wallet(c, C), { balance: increment(30), lastOp: old })); }
await ok('файл после покупки', getDoc(doc(a, 'vault', S + '_0')));
await no('автор меняет счётчик', updateDoc(doc(b, 'assets', S), { sold: 0, updated: now() }));

// ---------- бесплатный тираж 1
const F = 'bob_cape';
await ok('ассет B: плащ бесплатно, тираж 1', setDoc(doc(b, 'assets', F), assetDoc(B, { name: 'Плащ', price: 0, supply: 1 })));
function take(d, uid, asset, serial, count = true) {
  const w = writeBatch(d);
  w.set(doc(d, 'owns', uid + '_' + asset), { uid, asset, serial, via: 'free', op: '', ref: '', prev: '', paid: 0, at: now() });
  if (count) w.update(doc(d, 'assets', asset), { sold: increment(1), owners: increment(1) });
  return w.commit();
}
await no('взять без счётчика', take(c, C, F, 0, false));
await ok('C берёт плащ', take(c, C, F, 1));
await no('тираж кончился', take(a, A, F, 2));
await ok('бесплатный файл читается', (async () => { await setDoc(doc(b, 'vault', F + '_0'), { uid: B, asset: F, n: 0, data: 'B' }); return getDoc(doc(anon('Zed'), 'vault', F + '_0')); })());

// ---------- перепродажа
await no('выставить чужую копию', setDoc(doc(c, 'listings', C + '_' + S), { uid: C, asset: S, price: 50, serial: 1, at: now() }));
await ok('A выставляет меч за 50', setDoc(doc(a, 'listings', A + '_' + S), { uid: A, asset: S, price: 50, serial: 1, at: now() }));
function resale(d, uid, asset, seller, price, serial, opts = {}) {
  const w = writeBatch(d); const id = opId(d);
  w.set(doc(d, 'ops', id), { kind: 'resale', from: uid, to: seller, amount: price, asset, ref: '', by: uid, at: now() });
  w.update(wallet(d, uid), { balance: increment(-price), lastOp: id });
  w.update(wallet(d, seller), { balance: increment(price), lastOp: id });
  if (!opts.keepListing) w.delete(doc(d, 'listings', seller + '_' + asset));
  if (!opts.keepSeller) w.delete(doc(d, 'owns', seller + '_' + asset));
  w.set(doc(d, 'owns', uid + '_' + asset), { uid, asset, serial, via: 'resale', op: id, ref: '', prev: seller, paid: price, at: now() });
  return w.commit();
}
await no('перепродажа: продавец оставил себе копию', resale(c, C, S, A, 50, 1, { keepSeller: true }));
await no('перепродажа: объявление осталось', resale(c, C, S, A, 50, 1, { keepListing: true }));
await no('перепродажа: другой номер', resale(c, C, S, A, 50, 7));
await ok('C покупает меч у A', resale(c, C, S, A, 50, 1));
console.log('     балансы A B C', await bal(A), await bal(B), await bal(C));
await no('то же объявление ещё раз', resale(b, B, S, A, 50, 1));

// ---------- обмен предмет на предмет: B (новый щит) ↔ C (меч)
const H = 'bob_shield';
await ok('ассет B: щит за 10', setDoc(doc(b, 'assets', H), assetDoc(B, { name: 'Щит', price: 10 })));
await ok('A покупает щит', buy(a, A, H, B, 10, 1));
const T1 = 'trade1';
await no('обмен на то, чего у C нет', setDoc(doc(a, 'trades', 'bad'), { from: A, fromName: 'A', to: C, toName: 'C', give: H, giveSerial: 1, take: F + 'x', takeSerial: 1, credits: 0, text: '', status: 'open', at: now(), updated: now() }));
await ok('A предлагает щит за меч C', setDoc(doc(a, 'trades', T1), { from: A, fromName: 'A', to: C, toName: 'C', give: H, giveSerial: 1, take: S, takeSerial: 1, credits: 0, text: 'меняемся?', status: 'open', at: now(), updated: now() }));
await no('чужой обмен читать', getDoc(doc(b, 'trades', T1)));
function acceptSwap(d, half = false) {
  const w = writeBatch(d);
  w.update(doc(d, 'trades', T1), { status: 'accepted', updated: now() });
  w.delete(doc(d, 'owns', A + '_' + H));
  w.set(doc(d, 'owns', C + '_' + H), { uid: C, asset: H, serial: 1, via: 'trade', op: '', ref: T1, prev: A, paid: 0, at: now() });
  if (!half) {
    w.delete(doc(d, 'owns', C + '_' + S));
    w.set(doc(d, 'owns', A + '_' + S), { uid: A, asset: S, serial: 1, via: 'trade', op: '', ref: T1, prev: C, paid: 0, at: now() });
  }
  return w.commit();
}
await no('принять обмен наполовину (взять и не отдать)', acceptSwap(c, true));
await no('провести обмен за другого', acceptSwap(a));
await ok('C принимает обмен', acceptSwap(c));
const owner = async id => { let v; await admin(async d => { v = (await getDoc(doc(d, 'owns', id))).exists(); }); return v; };
console.log('     A меч', await owner(A + '_' + S), 'C щит', await owner(C + '_' + H), 'C меч', await owner(C + '_' + S));

// ---------- кредиты за предмет: B предлагает 40 за щит C
const T2 = 'trade2';
await ok('B: 40 кредитов за щит C', setDoc(doc(b, 'trades', T2), { from: B, fromName: 'B', to: C, toName: 'C', give: '', giveSerial: 0, take: H, takeSerial: 1, credits: 40, text: '', status: 'open', at: now(), updated: now() }));
function acceptCredits(d, pay = true) {
  const w = writeBatch(d); const id = opId(d);
  w.update(doc(d, 'trades', T2), { status: 'accepted', updated: now() });
  w.delete(doc(d, 'owns', C + '_' + H));
  w.set(doc(d, 'owns', B + '_' + H), { uid: B, asset: H, serial: 1, via: 'trade', op: '', ref: T2, prev: C, paid: 0, at: now() });
  if (pay) {
    w.set(doc(d, 'ops', id), { kind: 'trade', from: B, to: C, amount: 40, asset: H, ref: T2, by: C, at: now() });
    w.update(wallet(d, B), { balance: increment(-40), lastOp: id });
    w.update(wallet(d, C), { balance: increment(40), lastOp: id });
  }
  return w.commit();
}
await no('отдать щит и не получить кредиты? (принять без оплаты)', acceptCredits(c, false));
const before = [await bal(B), await bal(C)];
await ok('C принимает 40 за щит', acceptCredits(c));
console.log('     B', before[0], '→', await bal(B), ' C', before[1], '→', await bal(C));

// ---------- живые заказы
const O = 'order1';
const order = (uid, budget) => ({ uid, author: uid, title: 'Мод на погоду', text: 'Нужен дождь', game: 'valheim', budget, closes: Timestamp.fromMillis(Date.now() + 3600e3), days: 5, status: 'open', bids: 0, winner: '', winnerName: '', price: 0, due: now(), delivery: '', note: '', deliveredAt: now(), rating: 0, created: now(), updated: now() });
await no('заказ дороже кошелька', setDoc(doc(a, 'orders', 'big'), order(A, 100000)));
await ok('A размещает заказ на 60', setDoc(doc(a, 'orders', O), order(A, 60)));
await no('ставка выше бюджета', setDoc(doc(b, 'orders', O, 'bids', B), { uid: B, author: 'B', price: 70, days: 3, text: '', at: now() }));
await no('ставка на свой заказ', setDoc(doc(a, 'orders', O, 'bids', A), { uid: A, author: 'A', price: 10, days: 3, text: '', at: now() }));
{ const w = writeBatch(b);
  w.set(doc(b, 'orders', O, 'bids', B), { uid: B, author: 'B', price: 45, days: 3, text: 'сделаю', at: now() });
  w.update(doc(b, 'orders', O), { bids: increment(1) });
  await ok('B ставит 45', w.commit()); }
{ const w = writeBatch(c);
  w.set(doc(c, 'orders', O, 'bids', C), { uid: C, author: 'C', price: 40, days: 7, text: '', at: now() });
  w.update(doc(c, 'orders', O), { bids: increment(1) });
  await ok('C ставит 40', w.commit()); }
await no('накрутить счётчик ставок', updateDoc(doc(c, 'orders', O), { bids: increment(5) }));
function pick(d, uid, winner, price, days, escrow = true, amount = price) {
  const w = writeBatch(d); const id = opId(d);
  w.update(doc(d, 'orders', O), { status: 'assigned', winner, winnerName: winner[0], price, due: Timestamp.fromMillis(Date.now() + days * 86400e3), updated: now() });
  if (escrow) {
    w.set(doc(d, 'ops', id), { kind: 'escrow', from: uid, to: 'order:' + O, amount, asset: '', ref: O, by: uid, at: now() });
    w.update(wallet(d, uid), { balance: increment(-amount), lastOp: id });
  }
  return w.commit();
}
await no('выбрать ставку без заморозки', pick(a, A, B, 45, 3, false));
await no('заморозить меньше', pick(a, A, B, 45, 3, true, 5));
await no('выбрать с чужим сроком', pick(a, A, B, 45, 30));
await ok('A выбирает B за 45 (заморожено)', pick(a, A, B, 45, 3));
console.log('     A', await bal(A));
await no('ставка после выбора', setDoc(doc(c, 'orders', O, 'bids', C), { uid: C, author: 'C', price: 30, days: 7, text: '', at: now() }));
await no('C сдаёт чужой заказ', updateDoc(doc(c, 'orders', O), { status: 'delivered', delivery: 'x', note: '', deliveredAt: now(), updated: now() }));
function settle(d, by, status, kind, to, amount, extra = {}) {
  const w = writeBatch(d); const id = opId(d);
  w.update(doc(d, 'orders', O), Object.assign({ status, updated: now() }, extra));
  w.set(doc(d, 'ops', id), { kind, from: 'order:' + O, to, amount, asset: '', ref: O, by, at: now() });
  w.update(wallet(d, to), { balance: increment(amount), lastOp: id });
  return w.commit();
}
await no('A вернуть деньги до срока', settle(a, A, 'refunded', 'refund', A, 45));
await no('B забрать деньги сам, не сдав', settle(b, B, 'done', 'release', B, 45, { rating: 0 }));
await ok('B сдаёт работу', updateDoc(doc(b, 'orders', O), { status: 'delivered', delivery: 'bob_weather', note: 'готово', deliveredAt: now(), updated: now() }));
await no('B забрать до 72 часов', settle(b, B, 'done', 'release', B, 45, { rating: 0 }));
await no('A выплатить больше', settle(a, A, 'done', 'release', B, 90, { rating: 5 }));
await no('A выплатить себе', settle(a, A, 'done', 'release', A, 45, { rating: 5 }));
const bb = await bal(B);
await ok('A принимает работу (5★), B получает 45', settle(a, A, 'done', 'release', B, 45, { rating: 5 }));
console.log('     B', bb, '→', await bal(B));
await no('выплатить второй раз', settle(a, A, 'done', 'release', B, 45, { rating: 5 }));

// исполнитель отказался — деньги назад
const O2 = 'order2';
await ok('A второй заказ', setDoc(doc(a, 'orders', O2), order(A, 20)));
await ok('C ставка 20', setDoc(doc(c, 'orders', O2, 'bids', C), { uid: C, author: 'C', price: 20, days: 2, text: '', at: now() }));
{ const w = writeBatch(a); const id = opId(a);
  w.update(doc(a, 'orders', O2), { status: 'assigned', winner: C, winnerName: 'C', price: 20, due: Timestamp.fromMillis(Date.now() + 2 * 86400e3), updated: now() });
  w.set(doc(a, 'ops', id), { kind: 'escrow', from: A, to: 'order:' + O2, amount: 20, asset: '', ref: O2, by: A, at: now() });
  w.update(wallet(a, A), { balance: increment(-20), lastOp: id });
  await ok('A выбирает C', w.commit()); }
const ab = await bal(A);
{ const w = writeBatch(c); const id = opId(c);
  w.update(doc(c, 'orders', O2), { status: 'refunded', updated: now() });
  w.set(doc(c, 'ops', id), { kind: 'refund', from: 'order:' + O2, to: A, amount: 20, asset: '', ref: O2, by: C, at: now() });
  w.update(wallet(c, A), { balance: increment(20), lastOp: id });
  await ok('C отказывается — A получает 20 назад', w.commit()); }
console.log('     A', ab, '→', await bal(A));

// журнал
const { query, where, getDocs } = await import('firebase/firestore');
await ok('свой журнал (from)', getDocs(query(collection(a, 'ops'), where('from', '==', A))));
await no('чужой журнал', getDocs(query(collection(a, 'ops'), where('from', '==', B))));

// выбросить копию: −1 владелец
{ const w = writeBatch(a);
  w.delete(doc(a, 'owns', A + '_' + S));
  w.update(doc(a, 'assets', S), { owners: increment(-1) });
  await ok('A выбрасывает меч', w.commit()); }

console.log(`\n${pass} ok, ${fail} failed`);
await env.cleanup();
process.exit(fail ? 1 : 0);
