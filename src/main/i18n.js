'use strict';

/**
 * Язык сообщений основного процесса.
 *
 * Интерфейс переводится словарём в renderer/i18n.js, но часть текста рождается
 * здесь — объяснения ошибок доступа, отказов сети, проблем с архивом. Если
 * оставить их только по-русски, английский интерфейс развалится ровно в тот
 * момент, когда что-то пойдёт не так, то есть когда текст важнее всего.
 *
 * Шаги прогресса сюда не попадают: они уходят наружу кодами (`search.disks`,
 * `install.download`), а словами становятся уже в интерфейсе. Так у нас нет
 * двух наборов переводов на одно и то же, и нет сравнения строк в коде —
 * раньше конец установки ловился как `payload.step === 'Готово'`, и один
 * переименованный шаг ломал индикатор молча.
 */

const DICT = {
  ru: {
    'err.unknownGame': 'Неизвестная игра: {id}',
    'err.linkRejected': 'Ссылка отклонена.',
    'err.folderMissing': 'Папка не найдена.',
    'err.elevateFailed': 'Не удалось запросить права администратора.\n\n{reason}',
    'err.pickGameFolder': 'Сначала нужно указать папку с игрой.',
    'err.gameNotFound': 'Игра не найдена.',
    'err.installLoaderFirst': 'Сначала нужно установить {loader}.',
    'err.modNotInCatalog': 'Мод не найден в каталоге.',
    'err.linkUnparsed': 'Ссылка не распознана.',
    'err.pathNotExists': 'Такой папки не существует.',
    'err.notGameFolder':
      'В этой папке не видно файлов {game}. Обычно нужна папка, в которой лежит .exe игры.',
    'err.archiveStructure':
      'Не удалось понять структуру архива — внутри не найдено ни одного мода.',
    'err.modNotFound': 'Мод не найден: {id}',

    'err.smapi.noAsset':
      'Не удалось найти установщик SMAPI в последнем релизе. Скачайте его вручную со smapi.io и запустите — ModHub подхватит установку.',
    'err.smapi.noExe':
      'В архиве SMAPI не найден установщик. Возможно, изменился формат релиза.',
    'err.smapi.notWindows':
      'На этой системе SMAPI не умеет ставиться без вопросов. Запустите установщик вручную: {path}',
    'err.smapi.noFiles':
      'Установщик SMAPI отработал, но файлы в папке игры не появились и ничего не объяснил.\n\nЧаще всего это значит, что указана не та папка: SMAPI ставится рядом с «Stardew Valley.exe». Проверьте путь на экране «Игры» — или запустите установщик вручную кнопкой ниже.',
    'err.smapi.failed':
      'Установщик SMAPI не довёл установку до конца. Вот что сказал он сам:\n\n{reason}\n\nЕго можно запустить вручную — кнопка ниже. Он откроется обычным окном, покажет то же сообщение целиком и спросит то, на что ModHub не смог ответить за вас.',

    'err.hk.noManaged': 'Не найдена папка Managed внутри игры: {path}',
    'err.hk.noApiBuild': 'ApiLinks не содержит сборку под эту систему.',

    'err.bepinex.noPack': 'В каталоге {community} не найден пакет загрузчика {pkg}',

    'err.nexus.noKey':
      'Не указан ключ Nexus Mods. Его нужно создать в настройках профиля на сайте Nexus и вставить в настройках ModHub — по правилам Nexus ключ заводит сам пользователь.',
    'err.nexus.noLink':
      'Nexus не вернул ссылку на файл. Возможно, ссылка устарела — нажмите кнопку на сайте ещё раз.',
    'err.nexus.noFiles': 'У этого мода на Nexus нет файлов для скачивания.',
    'err.nexus.offline': 'Nexus Mods не отвечает ({reason}). Проверьте интернет и попробуйте ещё раз.',
    'err.nexus.http': 'Nexus Mods ответил ошибкой ({status}). Попробуйте ещё раз через минуту.',
    'err.archiveFormat': 'Не получилось распаковать {file}. Такой архив Windows открыть не может — распакуйте его вручную и поставьте через «Из файла».',
    'err.dl.timeout': 'Файл так и не появился в «Загрузках». Можно указать его вручную — кнопкой «Выбрать файл».',

    'err.reviews.NOT_CONFIGURED':
      'Сервер отзывов ещё не подключён. Как подключить — в файле ОТЗЫВЫ-СЕРВЕР.txt рядом с BUILD.bat.',
    'err.reviews.OFFLINE': 'Сервер отзывов не отвечает ({reason}). Проверьте интернет и попробуйте ещё раз.',
    'err.reviews.AUTH_DISABLED':
      'Сервер отзывов не пускает анонимных пользователей. В Firebase: Authentication → Sign-in method → Anonymous → Enable.',
    'err.reviews.BAD_KEY': 'Ключ сервера отзывов не подходит. Проверьте apiKey в reviews.config.json.',
    'err.reviews.NO_DATABASE': 'На сервере отзывов нет базы. В Firebase: Build → Firestore Database → Create database.',
    'err.reviews.DENIED':
      'Сервер отзывов отклонил запись. Обычно это значит, что в Firebase не вставлены правила из файла firestore.rules.',
    'err.reviews.BUSY': 'Отзывы пока что временно недоступны. Попробуйте позже — оценки, которые уже видны, остаются на месте.',
    'err.reviews.NOT_FOUND': 'Отзыв не найден на сервере — возможно, его уже удалили.',
    'err.reviews.BAD_INPUT': 'Поставьте оценку от 1 до 5 звёзд и укажите имя.',
    'err.reviews.SERVER': 'Сервер отзывов ответил ошибкой ({reason}). Попробуйте ещё раз.',
    'err.reviews.SESSION': 'Вход в аккаунт устарел — войдите ещё раз: Настройки → Аккаунты.',
    'err.reviews.NOT_INSTALLED':
      'Оценить можно только мод, который вы скачали через ModHub и попробовали в игре. Сначала установите мод.',
    'err.reviews.NOT_PLAYED':
      'Сначала сыграйте с модом хотя бы раз: запустите игру кнопкой «Играть» (или как обычно — через Steam), потом возвращайтесь за оценкой.',
    'err.account.NOT_CONFIGURED': 'Аккаунты пока что временно недоступны.',
    'err.account.OFFLINE': 'Нет связи с сервером входа. Проверьте интернет и попробуйте ещё раз.',
    'err.account.EMAIL_EXISTS': 'Аккаунт с этой почтой уже есть — войдите в него.',
    'err.account.BAD_EMAIL': 'Проверьте адрес почты — в нём ошибка.',
    'err.account.WEAK_PASSWORD': 'Пароль должен быть не короче 8 знаков.',
    'err.account.WRONG_LOGIN': 'Неверная почта или пароль.',
    'err.account.NO_NAME': 'Напишите имя или ник — его увидят рядом с вашими отзывами.',
    'err.account.DISABLED': 'Этот аккаунт отключён.',
    'err.account.TOO_MANY': 'Слишком много попыток. Подождите пару минут и попробуйте снова.',
    'err.account.NOT_ENABLED': 'Аккаунты пока что временно недоступны.',
    'err.account.RELOGIN': 'Для этого нужно войти заново — выйдите и войдите ещё раз.',
    'err.account.SESSION': 'Вход устарел — войдите в аккаунт ещё раз.',
    'err.account.ALREADY': 'Вы уже вошли в аккаунт. Чтобы создать другой, сначала выйдите.',
    'err.account.BUSY': 'Аккаунты пока что временно недоступны. Попробуйте позже.',
    'err.account.BAD_KEY': 'Аккаунты пока что временно недоступны.',
    'err.account.SERVER': 'Сервер входа ответил ошибкой ({reason}). Попробуйте ещё раз.',

    'err.loaderExeMissing':
      'Не найден {loader} — похоже, загрузчик ещё не установлен.\n\nОжидался файл:\n{path}',
    'err.gameExeMissing':
      'Не найден файл запуска игры:\n{path}\n\nВ папке игры лежат: {present}.\n\nЕсли игра называется иначе — значит указана не та папка. Поменять её можно на экране «Игры».',
    'err.gameExeMissingEmpty':
      'Не найден файл запуска игры:\n{path}\n\nВ этой папке вообще нет исполняемых файлов — скорее всего, указана не та папка.',

    'err.busy':
      'Файл занят другой программой. Обычно это сама игра — закройте её (и лаунчер Steam, если он открыт) и попробуйте снова.',
    'err.noAccess.head': 'Нет прав на запись в папку игры.\n\n',
    'err.noAccess.protected':
      'Игра установлена в {where} — Windows защищает это место и не даёт менять файлы без прав администратора. ',
    'err.noAccess.plain': 'Windows не даёт ModHub менять файлы в этой папке. ',
    'err.noAccess.tail':
      'Загрузчик модов обязан класть файлы именно в папку игры, обойти это нельзя.\n\nРешение: перезапустить ModHub от имени администратора — кнопка ниже. Windows один раз спросит подтверждение.',

    'dl.serverRefused': 'Сервер не отдал файл: {status} {statusText}.\nАдрес: {url}',
    'dl.failed': 'Не удалось скачать файл с {host}.',
    'dl.truncated':
      'Закачка оборвалась: получено {got} из {want}. Пробовал {attempts} раза подряд, соединение с {host} каждый раз рвётся.\n\nЭто не поломка мода, а проблема связи. Что помогает: проверить, открывается ли {host} в браузере, и попробовать ещё раз через минуту. Если сайт не открывается и там — доступ к нему закрыт на стороне сети.',
    'dl.mismatch':
      'Файл скачался целиком ({size}), но его содержимое не совпадает с тем, что указано в каталоге.\n\nОжидалось: {expected}…\nПолучено:  {actual}…\n\nТакое бывает, когда между вами и {host} стоит что-то, что подменяет ответ — антивирус с проверкой HTTPS, корпоративный прокси или фильтрация провайдера. Установка отменена: ставить файл, содержимое которого не подтверждается, нельзя.',

    'bytes.unknown': 'неизвестно сколько',
    'bytes.b': '{n} Б',
    'bytes.kb': '{n} КБ',
    'bytes.mb': '{n} МБ',

    'loader.installed': 'установлен',
    'window.admin': ' — администратор',
    'dialog.pickGame': 'Укажите папку с игрой',
    'dialog.pickArchive': 'Выберите архив с модом',
    'dialog.archives': 'Архивы модов',
    'dialog.pack': 'Сборка ModHub',
    'dialog.packExport': 'Сохранить сборку модов',
    'dialog.packImport': 'Открыть сборку модов',
    'err.PROFILE_NAME': 'Дайте профилю название.',
    'err.PROFILE_LIMIT': 'Профилей слишком много: удалите ненужные.',
    'err.PROFILE_MISSING': 'Такого профиля больше нет.',
    'err.BACKUP_NO_SAVES': 'Сохранений пока нет — копировать нечего. Сыграйте хотя бы раз.',
    'err.BACKUP_MISSING': 'Эта резервная копия не найдена.',
    'err.BACKUP_BROKEN': 'Резервная копия повреждена — восстановить её нельзя.',
    'err.BACKUP_RUNNING': 'Игра сейчас запущена. Закройте её, потом восстанавливайте сохранения.',
    'err.PACK_FORMAT': 'Это не файл сборки ModHub.',
    'err.PACK_NEWER': 'Сборка сделана в более новой версии ModHub — обновите программу.',
    'err.PACK_GAME': 'Сборка сделана для игры, которую эта версия ModHub не поддерживает.',
    'err.presetEmpty': 'В архиве нет пресета ReShade (файла .ini со списком эффектов).',
    'err.presetUnsupported': 'Для этой игры ModHub пока не умеет ставить шейдеры.',
    'err.updateManual': 'Моды с Nexus обновляются через сайт: откройте страницу мода.',
  },

  en: {
    'err.unknownGame': 'Unknown game: {id}',
    'err.linkRejected': 'Link rejected.',
    'err.folderMissing': 'Folder not found.',
    'err.elevateFailed': 'Could not request administrator rights.\n\n{reason}',
    'err.pickGameFolder': 'Point ModHub to the game folder first.',
    'err.gameNotFound': 'Game not found.',
    'err.installLoaderFirst': 'Install {loader} first.',
    'err.modNotInCatalog': 'That mod is not in the catalogue.',
    'err.linkUnparsed': 'Link not recognised.',
    'err.pathNotExists': 'That folder does not exist.',
    'err.notGameFolder':
      'No {game} files in this folder. ModHub needs the folder that holds the game .exe.',
    'err.archiveStructure': 'Could not make sense of the archive — no mod inside it.',
    'err.modNotFound': 'Mod not found: {id}',

    'err.smapi.noAsset':
      'No SMAPI installer in the latest release. Download it from smapi.io and run it — ModHub will pick the install up.',
    'err.smapi.noExe': 'No installer inside the SMAPI archive. The release layout may have changed.',
    'err.smapi.notWindows':
      'SMAPI cannot install unattended on this system. Run the installer by hand: {path}',
    'err.smapi.noFiles':
      'The SMAPI installer finished, but no files appeared in the game folder and it explained nothing.\n\nUsually this means the wrong folder: SMAPI installs next to "Stardew Valley.exe". Check the path on the Games screen — or run the installer by hand with the button below.',
    'err.smapi.failed':
      'The SMAPI installer did not finish. Here is what it said itself:\n\n{reason}\n\nYou can run it by hand — the button below. It opens in a normal window, shows the whole message and asks what ModHub could not answer for you.',

    'err.hk.noManaged': 'No Managed folder inside the game: {path}',
    'err.hk.noApiBuild': 'ApiLinks has no build for this system.',

    'err.bepinex.noPack': 'Loader package {pkg} is not in the {community} catalogue',

    'err.nexus.noKey':
      'No Nexus Mods key. Create one in your profile settings on the Nexus site and paste it into ModHub settings — by their rules the key has to be yours.',
    'err.nexus.noLink':
      'Nexus returned no file link. The link has probably expired — press the button on the site again.',
    'err.nexus.noFiles': 'This mod has no downloadable files on Nexus.',
    'err.nexus.offline': 'Nexus Mods is not responding ({reason}). Check your connection and try again.',
    'err.nexus.http': 'Nexus Mods returned an error ({status}). Try again in a minute.',
    'err.archiveFormat': 'Could not unpack {file}. Windows cannot open this archive — unpack it yourself and install via “From file”.',
    'err.dl.timeout': 'The file never appeared in Downloads. You can point to it with “Choose file”.',

    'err.reviews.NOT_CONFIGURED':
      'The review server is not connected yet. How to connect it: see the review server guide next to BUILD.bat.',
    'err.reviews.OFFLINE': 'The review server is not responding ({reason}). Check your connection and try again.',
    'err.reviews.AUTH_DISABLED':
      'The review server does not accept anonymous users. In Firebase: Authentication → Sign-in method → Anonymous → Enable.',
    'err.reviews.BAD_KEY': 'The review server key does not fit. Check apiKey in reviews.config.json.',
    'err.reviews.NO_DATABASE': 'The review server has no database. In Firebase: Build → Firestore Database → Create database.',
    'err.reviews.DENIED':
      'The review server refused the write. Usually this means the rules from firestore.rules are not pasted into Firebase.',
    'err.reviews.BUSY': 'Reviews are temporarily unavailable. Try again later — the ratings you already see stay in place.',
    'err.reviews.NOT_FOUND': 'The review was not found on the server — it may have been removed already.',
    'err.reviews.BAD_INPUT': 'Give the mod 1 to 5 stars and enter your name.',
    'err.reviews.SERVER': 'The review server returned an error ({reason}). Try again.',
    'err.reviews.SESSION': 'Your sign-in has expired — sign in again: Settings → Accounts.',
    'err.reviews.NOT_INSTALLED':
      'You can only rate a mod you downloaded through ModHub and tried in the game. Install the mod first.',
    'err.reviews.NOT_PLAYED':
      'Play with the mod at least once first: launch the game with the "Play" button (or as usual, through Steam), then come back to rate it.',
    'err.account.NOT_CONFIGURED': 'Accounts are temporarily unavailable.',
    'err.account.OFFLINE': 'No connection to the sign-in server. Check your internet and try again.',
    'err.account.EMAIL_EXISTS': 'An account with this email already exists — sign in to it.',
    'err.account.BAD_EMAIL': 'Check the email address — something is wrong with it.',
    'err.account.WEAK_PASSWORD': 'The password must be at least 8 characters.',
    'err.account.WRONG_LOGIN': 'Wrong email or password.',
    'err.account.NO_NAME': 'Enter a name or nickname — it is shown next to your reviews.',
    'err.account.DISABLED': 'This account is disabled.',
    'err.account.TOO_MANY': 'Too many attempts. Wait a couple of minutes and try again.',
    'err.account.NOT_ENABLED': 'Accounts are temporarily unavailable.',
    'err.account.RELOGIN': 'This needs a fresh sign-in — sign out and sign in again.',
    'err.account.SESSION': 'Your sign-in has expired — sign in again.',
    'err.account.ALREADY': 'You are already signed in. Sign out first to create another account.',
    'err.account.BUSY': 'Accounts are temporarily unavailable. Try again later.',
    'err.account.BAD_KEY': 'Accounts are temporarily unavailable.',
    'err.account.SERVER': 'The sign-in server returned an error ({reason}). Try again.',

    'err.loaderExeMissing':
      '{loader} not found — the loader is probably not installed yet.\n\nExpected file:\n{path}',
    'err.gameExeMissing':
      'Game executable not found:\n{path}\n\nThis folder holds: {present}.\n\nIf the game is named differently, the folder is wrong. Change it on the Games screen.',
    'err.gameExeMissingEmpty':
      'Game executable not found:\n{path}\n\nThere are no executables in this folder at all — most likely the wrong folder.',

    'err.busy':
      'A file is held by another program — usually the game itself. Close it (and the Steam client) and try again.',
    'err.noAccess.head': 'No write access to the game folder.\n\n',
    'err.noAccess.protected':
      'The game sits in {where} — Windows protects that place and will not let anything change files there without administrator rights. ',
    'err.noAccess.plain': 'Windows will not let ModHub change files in this folder. ',
    'err.noAccess.tail':
      'A mod loader has to put its files inside the game folder, and there is no way around that.\n\nFix: restart ModHub as administrator — the button below. Windows will ask once.',

    'dl.serverRefused': 'The server refused the file: {status} {statusText}.\nURL: {url}',
    'dl.failed': 'Could not download the file from {host}.',
    'dl.truncated':
      'The download broke off: got {got} of {want}. Tried {attempts} times, the connection to {host} drops every time.\n\nThis is the network, not the mod. What helps: check whether {host} opens in a browser, then try again in a minute. If the site does not open there either, it is blocked upstream.',
    'dl.mismatch':
      'The file downloaded in full ({size}), but its contents do not match the catalogue.\n\nExpected: {expected}…\nGot:      {actual}…\n\nThis happens when something between you and {host} rewrites the response — an antivirus with HTTPS inspection, a corporate proxy, or ISP filtering. Install cancelled: a file whose contents cannot be confirmed does not go into your game.',

    'bytes.unknown': 'unknown size',
    'bytes.b': '{n} B',
    'bytes.kb': '{n} KB',
    'bytes.mb': '{n} MB',

    'loader.installed': 'installed',
    'window.admin': ' — administrator',
    'dialog.pickGame': 'Choose the game folder',
    'dialog.pickArchive': 'Choose a mod archive',
    'dialog.archives': 'Mod archives',
    'dialog.pack': 'ModHub modpack',
    'dialog.packExport': 'Save modpack',
    'dialog.packImport': 'Open modpack',
    'err.PROFILE_NAME': 'Give the profile a name.',
    'err.PROFILE_LIMIT': 'Too many profiles: delete the ones you no longer need.',
    'err.PROFILE_MISSING': 'That profile no longer exists.',
    'err.BACKUP_NO_SAVES': 'There are no saves yet — nothing to back up. Play at least once.',
    'err.BACKUP_MISSING': 'That backup was not found.',
    'err.BACKUP_BROKEN': 'That backup is damaged and cannot be restored.',
    'err.BACKUP_RUNNING': 'The game is running. Close it before restoring saves.',
    'err.PACK_FORMAT': 'This is not a ModHub modpack file.',
    'err.PACK_NEWER': 'This modpack was made by a newer ModHub — please update.',
    'err.PACK_GAME': 'This modpack is for a game this ModHub version does not support.',
    'err.presetEmpty': 'The archive has no ReShade preset (an .ini file with a list of effects).',
    'err.presetUnsupported': 'ModHub cannot install shaders for this game yet.',
    'err.updateManual': 'Nexus mods update through the website: open the mod page.',
  },
};

let current = 'ru';

/** @param {'ru'|'en'} lang */
function setLang(lang) {
  current = lang === 'en' ? 'en' : 'ru';
  return current;
}

function getLang() {
  return current;
}

/**
 * Текст по ключу с подстановкой {параметров}.
 * Неизвестный ключ возвращается как есть — это заметно в интерфейсе,
 * но ничего не ломает.
 *
 * @param {string} key
 * @param {Record<string, string|number>} [params]
 */
function t(key, params = {}) {
  const table = DICT[current] ?? DICT.ru;
  const template = table[key] ?? DICT.ru[key] ?? key;
  return template.replace(/\{(\w+)\}/g, (match, name) =>
    name in params ? String(params[name]) : match
  );
}

module.exports = { t, setLang, getLang, DICT };
