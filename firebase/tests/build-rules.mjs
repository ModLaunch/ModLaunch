// Склеить правила хаба и рынка в один файл, как они стоят в консоли Firebase.
import fs from 'fs';
const read = name => fs.readFileSync(new URL(`../${name}`, import.meta.url), 'utf8');
const body = read('creations.rules') + '\n' + read('market.rules');
fs.writeFileSync(new URL('./all.rules', import.meta.url),
  `rules_version = '2';\nservice cloud.firestore {\n  match /databases/{database}/documents {\n${body}\n  }\n}\n`);
console.log('all.rules ready');
