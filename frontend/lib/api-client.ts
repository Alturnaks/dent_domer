// Клиент API: access-токен в памяти, refresh по httpOnly cookie с double-submit CSRF.
// Все запросы идут на тот же origin (/api/v1/...), next.config проксирует их на бэкенд.

import { errorMessage } from "@/lib/i18n";

export type ApiErrorBody = { error: { code: string; message: string; details?: Record<string, unknown> | null } };

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public details?: Record<string, unknown> | null,
  ) {
    super(message);
  }

  /** Локализованное сообщение по коду ошибки. */
  get userMessage(): string {
    return errorMessage(this.code, this.message);
  }
}

let accessToken: string | null = null;
let refreshPromise: Promise<boolean> | null = null;
const listeners = new Set<(authenticated: boolean) => void>();

export function setAccessToken(token: string | null) {
  accessToken = token;
  listeners.forEach((l) => l(token !== null));
}

export function getAccessToken() {
  return accessToken;
}

export function onAuthChange(listener: (authenticated: boolean) => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function readCookie(name: string): string | null {
  if (typeof document === "undefined") return null;
  const m = document.cookie.match(new RegExp(`(?:^|; )${name}=([^;]*)`));
  return m ? decodeURIComponent(m[1]) : null;
}

export async function refreshAccessToken(): Promise<boolean> {
  if (refreshPromise) return refreshPromise;
  refreshPromise = (async () => {
    try {
      const csrf = readCookie("dental_csrf");
      if (!csrf) return false;
      const res = await fetch("/api/v1/auth/refresh", {
        method: "POST",
        credentials: "same-origin",
        headers: { "X-CSRF-Token": csrf },
      });
      if (!res.ok) {
        setAccessToken(null);
        return false;
      }
      const body = (await res.json()) as { accessToken: string };
      setAccessToken(body.accessToken);
      return true;
    } catch {
      return false;
    } finally {
      setTimeout(() => (refreshPromise = null), 0);
    }
  })();
  return refreshPromise;
}

type Query = Record<string, string | number | boolean | null | undefined | (string | number)[]>;

export function buildUrl(path: string, query?: Query): string {
  const url = path.startsWith("/api/") ? path : `/api/v1${path.startsWith("/") ? path : `/${path}`}`;
  if (!query) return url;
  const params = new URLSearchParams();
  for (const [k, v] of Object.entries(query)) {
    if (v === undefined || v === null || v === "") continue;
    if (Array.isArray(v)) v.forEach((x) => params.append(k, String(x)));
    else params.append(k, String(v));
  }
  const qs = params.toString();
  return qs ? `${url}?${qs}` : url;
}

export type RequestOptions = {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE";
  body?: unknown;
  query?: Query;
  idempotencyKey?: string;
  signal?: AbortSignal;
  raw?: boolean;
};

export async function api<T = unknown>(path: string, options: RequestOptions = {}): Promise<T> {
  const doFetch = () => {
    const headers: Record<string, string> = {};
    if (accessToken) headers.Authorization = `Bearer ${accessToken}`;
    if (options.body !== undefined && !(options.body instanceof FormData)) headers["Content-Type"] = "application/json";
    if (options.idempotencyKey) headers["Idempotency-Key"] = options.idempotencyKey;
    return fetch(buildUrl(path, options.query), {
      method: options.method ?? "GET",
      headers,
      credentials: "same-origin",
      signal: options.signal,
      body:
        options.body === undefined ? undefined : options.body instanceof FormData ? options.body : JSON.stringify(options.body),
    });
  };

  let res = await doFetch();
  if (res.status === 401 && !path.startsWith("/auth/login")) {
    const ok = await refreshAccessToken();
    if (ok) res = await doFetch();
  }

  if (!res.ok) {
    let body: ApiErrorBody | null = null;
    try {
      body = (await res.json()) as ApiErrorBody;
    } catch {
      /* пустое тело */
    }
    throw new ApiError(res.status, body?.error?.code ?? `HTTP_${res.status}`, body?.error?.message ?? res.statusText, body?.error?.details);
  }

  if (options.raw) return res as unknown as T;
  if (res.status === 204) return undefined as T;
  const ct = res.headers.get("content-type") ?? "";
  if (ct.includes("application/json")) return (await res.json()) as T;
  return (await res.text()) as unknown as T;
}

/** Скачать файл (экспорт Excel/PDF) с авторизацией. */
export async function download(path: string, query?: Query, fallbackName = "export") {
  const res = await api<Response>(path, { query, raw: true });
  const blob = await res.blob();
  const cd = res.headers.get("content-disposition") ?? "";
  const m = cd.match(/filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i);
  const name = m ? decodeURIComponent(m[1] ?? m[2]) : fallbackName;
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

export function newIdempotencyKey(): string {
  return typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`;
}
