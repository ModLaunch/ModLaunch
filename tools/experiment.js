'use strict';

/** Разовые опыты на живых сайтах (2.1): ReShade, SMAPI, требования Nexus. */

const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');

async function text(url, opts) {
  const r = await fetch(url, opts);
  return { status: r.status, body: await r.text() };
}

async function reshade() {
  console.log('=== ReShade ===');
  const page = await text('https://reshade.me/');
  console.log('reshade.me', page.status, page.body.length);
  const links = [...page.body.matchAll(/href="([^"]*ReShade_Setup[^"]*\.exe)"/g)].map((m) => m[1]);
  console.log('links:', links);
  const link = links.find((l) => !/addon/i.test(l)) ?? links[0];
  if (!link) return;
  const url = new URL(link, 'https://reshade.me/').href;
  const r = await fetch(url);
  const buf = Buffer.from(await r.arrayBuffer());
  console.log('setup', url, r.status, buf.length);
  const file = path.join(os.tmpdir(), 'rs.exe');
  fs.writeFileSync(file, buf);
  const AdmZip = require('adm-zip');
  try {
    const zip = new AdmZip(file);
    console.log('zip entries:', zip.getEntries().map((e) => `${e.entryName} ${e.header.size}`).join(' | '));
  } catch (e) {
    console.log('AdmZip failed:', e.message);
    // Ищем начало zip вручную.
    const sig = Buffer.from([0x50, 0x4b, 0x03, 0x04]);
    let at = buf.indexOf(sig);
    const found = [];
    while (at !== -1 && found.length < 5) {
      found.push(at);
      at = buf.indexOf(sig, at + 1);
    }
    console.log('local headers at', found, 'eocd at', buf.lastIndexOf(Buffer.from([0x50, 0x4b, 0x05, 0x06])));
    const start = found[0];
    if (start > 0) {
      try {
        const zip = new AdmZip(buf.subarray(start));
        console.log('zip from offset entries:', zip.getEntries().map((e) => `${e.entryName} ${e.header.size}`).join(' | '));
      } catch (e2) {
        console.log('offset zip failed:', e2.message);
      }
    }
  }
  const list = await text('https://raw.githubusercontent.com/crosire/reshade-shaders/list/EffectPackages.ini');
  console.log('EffectPackages.ini', list.status, '\n' + list.body.slice(0, 2500));
}

async function smapi() {
  console.log('\n=== SMAPI API ===');
  const body = {
    mods: [
      { id: 'Pathoschild.ContentPatcher', updateKeys: [] },
      { id: 'spacechase0.GenericModConfigMenu', updateKeys: [] },
      { id: 'FlashShifter.StardewValleyExpandedCP', updateKeys: [] },
    ],
    apiVersion: '4.0.0',
    gameVersion: '1.6.14',
    platform: 'Windows',
    includeExtendedMetadata: true,
  };
  const r = await fetch('https://smapi.io/api/v3.0/mods', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'User-Agent': 'ModHub/2.1 (probe)' },
    body: JSON.stringify(body),
  });
  const t = await r.text();
  console.log(r.status, t.slice(0, 3000));
}

async function nexusReq() {
  console.log('\n=== Nexus requirements ===');
  for (const [gid, mid] of [[1155, 2800], [1155, 1119], [1303, 3753], [1303, 1063], [2706, 44]]) {
    const r = await fetch('https://api.nexusmods.com/v2/graphql', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        query: `query { mod(modId: ${mid}, gameId: ${gid}) { name modRequirements { nexusRequirements { nodes { modId modName url gameId externalRequirement notes } } dlcRequirements { nodes { gameExpansion { name } } } } } }`,
      }),
    });
    const j = await r.json();
    console.log(gid, mid, JSON.stringify(j).slice(0, 900));
  }
}

(async () => {
  for (const f of [reshade, smapi, nexusReq]) {
    try {
      await f();
    } catch (e) {
      console.log(f.name, 'EXC', e.stack);
    }
  }
})();
