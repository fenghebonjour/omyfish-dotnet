// Default matches every backend's docker-compose gateway/origin port
// (Java api-gateway, .NET api-gateway, Django) — this frontend is shared
// verbatim across all three.
const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

export interface PredictionDto {
  speciesName: string;
  scientificName: string;
  confidence: number;
  confidencePercent: string;
  rank: number;
  conservationStatus?: string;
}

export interface IdentifyFishResult {
  predictions: PredictionDto[];
  uncertain: boolean;
  imageKey: string;
  isFish?: boolean;
}

export interface ObservationDto {
  id: string;
  userId: string;
  speciesName: string;
  scientificName?: string;
  topConfidence: number;
  imageStorageKey: string;
  imageUrl?: string;
  latitude?: number;
  longitude?: number;
  notes?: string;
  observedAt: string;
  createdAt: string;
}

export interface BiteHourlyScore {
  timestamp: string;
  score: number;
  breakdown: Record<string, number>;
  weightedContribution: Record<string, number>;
  timeOfDayMultiplier: number;
  safetyFlag?: string | null;
}

export interface TimeWindow {
  start: string;
  end: string;
}

export interface SunTimes {
  date: string; // ISO date
  sunrise: string;
  sunset: string;
}

export interface CurrentConditions {
  time: string;
  precipitationMm: number;
  isStorm: boolean;
  isHeavyPrecip: boolean;
}

export interface BiteForecast {
  species: string;
  lat: number;
  lon: number;
  hourly: BiteHourlyScore[];
  bestWindows: BiteHourlyScore[];
  majorWindows: TimeWindow[]; // per day: windows around the top-2 aggregate-score peaks
  minorWindows: TimeWindow[]; // per day: windows around the next-2 peaks
  sunTimes: SunTimes[]; // per-day sunrise/sunset (drives the dawn/dusk score boost)
  current: CurrentConditions | null; // live nowcast for "right now" alerts
}

export interface RegsSpeciesLimit {
  species: string;
  period: string;
  catchLimit: string;
  lengthLimit?: string | null;
  fishingDevice?: string | null;
  note?: string | null;
}

export interface RegsLimits {
  lat: number;
  lon: number;
  zoneName: string;
  zoneInfoUrl?: string | null;
  rules: RegsSpeciesLimit[];
  disclaimer: string;
}

export interface RegsStation {
  noBqma: string;
  hydronyme: string;
  latitude: number;
  longitude: number;
  distanceKm: number;
}

export interface RegsConsumption {
  lat: number;
  lon: number;
  species: string;
  stationName: string;
  distanceKm: number;
  sizeClass?: string | null;
  mealsPerMonth?: number | null;
  fishingStatus?: string | null;
  note?: string | null;
  disclaimer: string;
}

export interface RegsAskResponse {
  question: string;
  answer: string;
  sources: string[];
  disclaimer: string;
}

export interface NotificationDto {
  id: string;
  userId: string;
  type: string;
  title: string;
  body?: string | null;
  isRead: boolean;
  createdAt: string;
}

export interface TokenResponse {
  token: string;
  refreshToken: string;
  userId: string;
  email: string;
  role: string;
}

export interface UserDto {
  id: string;
  email: string;
  displayName?: string;
  role: string;
}

export interface SubscriptionDto {
  status: string; // trialing | active | canceled | expired
  plan: string | null;
  trialEnd: string | null;
  currentPeriodEnd: string | null;
}

export interface AdminStats {
  users: number;
  subscriptions: Record<string, number>;
  activePlans: { monthly: number; yearly: number };
  mrrCad: number;
}

export interface AdminSubscriptionRow {
  userId: string;
  email: string;
  status: string;
  plan: string | null;
  trialEnd: string | null;
  currentPeriodEnd: string | null;
}

async function apiFetch<T>(path: string, init?: RequestInit, token?: string): Promise<T> {
  const headers: HeadersInit = {
    ...(init?.headers ?? {}),
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
  };
  const res = await fetch(`${API_BASE}${path}`, { ...init, headers });
  if (!res.ok) {
    const text = await res.text().catch(() => res.statusText);
    throw new Error(`${res.status}: ${text}`);
  }
  return res.json() as Promise<T>;
}

export const api = {
  auth: {
    login: (email: string, password: string) =>
      apiFetch<TokenResponse>("/api/v1/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      }),

    register: (email: string, password: string, displayName?: string) =>
      apiFetch<UserDto>("/api/v1/auth/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password, displayName }),
      }),

    refresh: (refreshToken: string) =>
      apiFetch<TokenResponse>("/api/v1/auth/refresh", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ refreshToken }),
      }),

    me: (token: string) =>
      apiFetch<UserDto>("/api/v1/auth/me", {}, token),
  },

  species: {
    identify: (image: File, topK = 5, token?: string) => {
      const form = new FormData();
      form.append("image", image);
      form.append("topK", String(topK));
      return apiFetch<IdentifyFishResult>("/api/v1/species/identify", {
        method: "POST",
        body: form,
      }, token);
    },

    getAll: (northAmericanFreshwater?: boolean) => {
      const params = northAmericanFreshwater !== undefined
        ? `?northAmericanFreshwater=${northAmericanFreshwater}`
        : "";
      return apiFetch<PredictionDto[]>(`/api/v1/species${params}`);
    },
  },

  biteScore: {
    // species accepts a profile key or any common/scientific name from a
    // confirmed fish ID — the backend resolves it (general fallback).
    today: (lat: number, lon: number, species = "general") =>
      apiFetch<BiteForecast>(
        `/api/v1/species/bite-score/today?lat=${lat}&lon=${lon}&species=${encodeURIComponent(species)}`),

    forecast: (lat: number, lon: number, species = "general", hours = 336) =>
      apiFetch<BiteForecast>(
        `/api/v1/species/bite-score/forecast?lat=${lat}&lon=${lon}&species=${encodeURIComponent(species)}&hours=${hours}`),
  },

  // Quebec fishing regs/consumption advisor — proxied from omyfish-ai by
  // every backend at the same path, chatbot/retrieval logic lives there only.
  regs: {
    limits: (lat: number, lon: number, species = "general") =>
      apiFetch<RegsLimits>(
        `/api/v1/species/regs/limits?lat=${lat}&lon=${lon}&species=${encodeURIComponent(species)}`),

    zonesGeoJson: () =>
      apiFetch<GeoJSON.FeatureCollection>("/api/v1/species/regs/zones/geojson"),

    consumptionStations: (lat: number, lon: number, limit = 5) =>
      apiFetch<RegsStation[]>(
        `/api/v1/species/regs/consumption/stations?lat=${lat}&lon=${lon}&limit=${limit}`),

    consumption: (lat: number, lon: number, species = "general") =>
      apiFetch<RegsConsumption>(
        `/api/v1/species/regs/consumption?lat=${lat}&lon=${lon}&species=${encodeURIComponent(species)}`),

    ask: (question: string) =>
      apiFetch<RegsAskResponse>("/api/v1/species/regs/ask", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ question }),
      }),
  },

  notifications: {
    getAll: (token: string) =>
      apiFetch<NotificationDto[]>("/api/v1/notifications", {}, token),

    markRead: (id: string, token: string) =>
      fetch(`${API_BASE}/api/v1/notifications/${id}/read`, {
        method: "PUT",
        headers: { Authorization: `Bearer ${token}` },
      }).then((r) => { if (!r.ok) throw new Error(`${r.status}`); }),
  },

  billing: {
    me: (token: string) =>
      apiFetch<SubscriptionDto>("/api/v1/billing/me", {}, token),

    checkout: (plan: "monthly" | "yearly", token: string) =>
      apiFetch<{ checkoutUrl: string }>("/api/v1/billing/checkout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ plan }),
      }, token),
  },

  admin: {
    stats: (token: string) =>
      apiFetch<AdminStats>("/api/v1/admin/stats", {}, token),

    subscriptions: (token: string) =>
      apiFetch<AdminSubscriptionRow[]>("/api/v1/admin/subscriptions", {}, token),

    grant: (userId: string, token: string, days = 365, plan = "yearly") =>
      apiFetch<SubscriptionDto>(`/api/v1/admin/subscriptions/${userId}/grant`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ days, plan }),
      }, token),

    revoke: (userId: string, token: string) =>
      apiFetch<SubscriptionDto>(`/api/v1/admin/subscriptions/${userId}/revoke`, {
        method: "POST",
      }, token),

    extendTrial: (userId: string, token: string, days = 7) =>
      apiFetch<SubscriptionDto>(`/api/v1/admin/subscriptions/${userId}/extend-trial`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ days }),
      }, token),
  },

  observations: {
    getAll: (token: string, myOnly = true) =>
      apiFetch<ObservationDto[]>(`/api/v1/observations?myOnly=${myOnly}`, {}, token),

    getById: (id: string, token: string) =>
      apiFetch<ObservationDto>(`/api/v1/observations/${id}`, {}, token),

    delete: (id: string, token: string) =>
      fetch(`${API_BASE}/api/v1/observations/${id}`, {
        method: "DELETE",
        headers: { Authorization: `Bearer ${token}` },
      }).then((r) => { if (!r.ok && r.status !== 204) throw new Error(`${r.status}`); }),

    getGeoJson: () =>
      apiFetch<object>("/api/v1/observations/geojson"),
  },
};
