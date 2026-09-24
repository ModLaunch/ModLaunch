'use strict';

/**
 * Иконка и сведения о файле для ModHub.exe.
 *
 * electron-builder правит их через rcedit, а rcedit на Linux требует Wine.
 * resedit делает то же самое на чистом JavaScript — поэтому сборка
 * ModHub-Setup.exe работает на любой системе.
 */

const fs = require('node:fs');
const path = require('node:path');

exports.default = async function afterPack(context) {
  if (context.electronPlatformName !== 'win32') return;
  const ResEdit = await import('resedit');
  const lib = ResEdit.default ?? ResEdit;
  const { version, productName } = context.packager.appInfo;
  const exe = path.join(context.appOutDir, `${context.packager.appInfo.productFilename}.exe`);
  const root = context.packager.projectDir;

  const data = fs.readFileSync(exe);
  const file = lib.NtExecutable.from(data, { ignoreCert: true });
  const res = lib.NtExecutableResource.from(file);

  const iconFile = lib.Data.IconFile.from(fs.readFileSync(path.join(root, 'src', 'assets', 'icon.ico')));
  const groups = lib.Resource.IconGroupEntry.fromEntries(res.entries);
  const iconId = groups[0]?.id ?? 1;
  const lang = groups[0]?.lang ?? 1033;
  lib.Resource.IconGroupEntry.replaceIconsForResource(
    res.entries,
    iconId,
    lang,
    iconFile.icons.map((item) => item.data)
  );

  const [info] = lib.Resource.VersionInfo.fromEntries(res.entries);
  if (info) {
    const [major, minor, patch] = version.split('.').map((n) => Number(n) || 0);
    info.setFileVersion(major, minor, patch, 0, 1033);
    info.setProductVersion(major, minor, patch, 0, 1033);
    info.setStringValues(
      { lang: 1033, codepage: 1200 },
      {
        FileDescription: productName,
        ProductName: productName,
        CompanyName: productName,
        LegalCopyright: productName,
        OriginalFilename: `${productName}.exe`,
        InternalName: productName,
        FileVersion: version,
        ProductVersion: version,
      }
    );
    info.outputToResourceEntries(res.entries);
  }

  res.outputResource(file);
  fs.writeFileSync(exe, Buffer.from(file.generate()));
};
