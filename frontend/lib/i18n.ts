import ruBase from "@/messages/ru.json";
import ruSchedule from "@/messages/ru/schedule.json";
import ruVisits from "@/messages/ru/visits.json";
import ruCash from "@/messages/ru/cash.json";
import ruInventory from "@/messages/ru/inventory.json";
import ruAdmin from "@/messages/ru/admin.json";
import ruPurchasing from "@/messages/ru/purchasing.json";

// Модульные файлы переводов (messages/ru/<модуль>.json) сливаются с базовым по ключам верхнего уровня.
const ru = { ...ruBase, ...ruSchedule, ...ruVisits, ...ruCash, ...ruInventory, ...ruAdmin, ...ruPurchasing } as Record<string, unknown>;

// i18n: ru по умолчанию; kk и en добавляются новыми файлами messages/<lang>.json с теми же ключами.
type Messages = Record<string, unknown>;
const dictionaries: Record<string, Messages> = { ru };
let current = "ru";

export function setLocale(locale: string) {
  if (dictionaries[locale]) current = locale;
}

function lookup(dict: Messages, key: string): string | undefined {
  let node: unknown = dict;
  for (const part of key.split(".")) {
    if (node && typeof node === "object" && part in (node as Messages)) node = (node as Messages)[part];
    else return undefined;
  }
  return typeof node === "string" ? node : undefined;
}

export type TFunction = (key: string, vars?: Record<string, string | number>) => string;

export const t: TFunction = (key, vars) => {
  let s = lookup(dictionaries[current], key) ?? lookup(dictionaries.ru, key) ?? key;
  if (vars) for (const [k, v] of Object.entries(vars)) s = s.replaceAll(`{${k}}`, String(v));
  return s;
};

/** Есть ли перевод (для кодов ошибок API). */
export function hasKey(key: string): boolean {
  return lookup(dictionaries[current], key) !== undefined;
}

export function useT(): TFunction {
  return t;
}

/** Сообщение об ошибке API по коду: errors.<CODE>, иначе текст с сервера. */
export function errorMessage(code: string | undefined, fallback?: string): string {
  if (code && hasKey(`errors.${code}`)) return t(`errors.${code}`);
  return fallback ?? t("errors.INTERNAL_ERROR");
}
