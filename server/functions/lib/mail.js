// Письма: вход с нового устройства, подтверждение листа ожидания. Отправка — через SMTP (секрет SMTP_URL,
// например smtps://user:pass@smtp.provider.com:465). Тексты — RU/EN по языку человека.
import { createHash, createHmac, timingSafeEqual } from 'node:crypto';
import nodemailer from 'nodemailer';

export function transport(smtpUrl) {
  return nodemailer.createTransport(smtpUrl);
}

const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

export function newDeviceMail(lang, { name, os, app, when }) {
  const ru = lang !== 'en';
  const subject = ru ? 'Новый вход в аккаунт ModLaunch' : 'New sign-in to your ModLaunch account';
  const text = ru
    ? `В ваш аккаунт ModLaunch вошли с нового устройства: ${name} (${os}, ModLaunch ${app}), ${when}.\nЕсли это были не вы — откройте ModLaunch → Аккаунт → Безопасность и смените пароль. Новый пароль закроет вход на чужом устройстве.`
    : `Someone signed in to your ModLaunch account from a new device: ${name} (${os}, ModLaunch ${app}), ${when}.\nIf it wasn't you, open ModLaunch → Account → Security and change your password. The new password signs the other device out.`;
  return { subject, text, html: `<p>${esc(text).replace(/\n/g, '<br>')}</p>` };
}

/** Токен подтверждения адреса для листа ожидания: HMAC(secret, email) — без хранения отдельного токена. */
export function waitlistToken(secret, email) {
  return createHmac('sha256', secret).update(String(email).trim().toLowerCase()).digest('hex').slice(0, 32);
}

export function checkWaitlistToken(secret, email, token) {
  const want = Buffer.from(waitlistToken(secret, email));
  const got = Buffer.from(String(token ?? ''));
  return got.length === want.length && timingSafeEqual(got, want);
}

export const emailKey = (email) => createHash('sha256').update(String(email).trim().toLowerCase()).digest('hex').slice(0, 40);

export function waitlistMail(lang, confirmUrl) {
  const ru = lang !== 'en';
  const subject = ru ? 'Подтвердите адрес — лист ожидания ModLaunch' : 'Confirm your e-mail — ModLaunch waitlist';
  const text = ru
    ? `Спасибо, что записались! Чтобы получить приглашение в день запуска, подтвердите адрес:\n${confirmUrl}\n\nЕсли вы не записывались — просто удалите это письмо.`
    : `Thanks for signing up! To get your invite on launch day, confirm your address:\n${confirmUrl}\n\nIf you didn't sign up, just delete this e-mail.`;
  return { subject, text, html: `<p>${esc(text).replace(/\n/g, '<br>').replace(esc(confirmUrl), `<a href="${esc(confirmUrl)}">${esc(confirmUrl)}</a>`)}</p>` };
}

export const isEmail = (s) => /^[^\s@]{1,64}@[^\s@]{1,190}\.[^\s@]{2,}$/.test(String(s ?? '').trim()) && String(s).length <= 254;
