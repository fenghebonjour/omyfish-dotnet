const API_BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5000";

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

export interface BiteForecast {
  species: string;
  lat: number;
  lon: number;
  hourly: BiteHourlyScore[];
  bestWindows: BiteHourlyScore[];
}

export interface TokenResponse {
  accessToken: string;
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

    forecast: (lat: number, lon: number, species = "general", hours = 168) =>
      apiFetch<BiteForecast>(
        `/api/v1/species/bite-score/forecast?lat=${lat}&lon=${lon}&species=${encodeURIComponent(species)}&hours=${hours}`),
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
