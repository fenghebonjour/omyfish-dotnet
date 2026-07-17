"use client";

import { useEffect, useMemo, useState } from "react";
import { api, type BiteForecast, type BiteHourlyScore, type TimeWindow } from "@/lib/api";
import { FACTORS, activityBand, dayWindowMean, isoDateOf, type Factor } from "@/lib/biteScore";
import { ActivityChart, type ChartPoint, type HourRange } from "@/components/timing/ActivityChart";
import { ActivityGauge } from "@/components/timing/ActivityGauge";
import { CalendarPopover } from "@/components/timing/CalendarPopover";
import { DayStrip } from "@/components/timing/DayStrip";

type FactorTab = "overall" | Factor;

const REVERSE_GEOCODE_URL = "https://api.bigdatacloud.net/data/reverse-geocode-client";

function gaugeLabel(tab: FactorTab, score: number | null): string {
  if (score === null) return "No data for this day";
  const band = activityBand(score);
  if (tab === "overall") return `${band} fish activity`;
  const quality = band === "High" ? "Favorable" : band === "Medium" ? "Fair" : "Poor";
  return `${quality} ${tab} conditions`;
}

function formatTime(iso: string) {
  return new Date(iso).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}

function formatWindow(w: TimeWindow) {
  const start = formatTime(w.start);
  const end = formatTime(w.end);
  const suffix = / (AM|PM)$/;
  const sameMeridiem = start.match(suffix)?.[1] === end.match(suffix)?.[1];
  return sameMeridiem ? `${start.replace(suffix, "")}–${end}` : `${start} – ${end}`;
}

function windowsForDay(windows: TimeWindow[], day: string): TimeWindow[] {
  const dayStart = new Date(`${day}T00:00:00`).getTime();
  const dayEnd = dayStart + 24 * 3600 * 1000;
  return windows.filter(
    (w) => new Date(w.start).getTime() < dayEnd && new Date(w.end).getTime() > dayStart,
  );
}

function hourOfDay(iso: string, day: string): number {
  return (new Date(iso).getTime() - new Date(`${day}T00:00:00`).getTime()) / 3_600_000;
}

/** Map a major/minor window onto the day's 0-24 hour axis, clipped to the day. */
function toHourRange(w: TimeWindow, day: string): HourRange {
  return {
    x1: Math.max(0, hourOfDay(w.start, day)),
    x2: Math.min(24, hourOfDay(w.end, day)),
  };
}

interface SafetyAlert {
  message: string;
  startMs: number;
  endMs: number; // exclusive end of the last flagged hour block
}

/** Group consecutive flagged hours of a day into ranges, per flag message. */
function safetyAlerts(hours: BiteHourlyScore[]): SafetyAlert[] {
  const alerts: SafetyAlert[] = [];
  let open: SafetyAlert | null = null;
  for (const h of hours) {
    const startMs = new Date(h.timestamp).getTime();
    if (h.safetyFlag && open && open.message === h.safetyFlag && open.endMs === startMs) {
      open.endMs = startMs + 3_600_000;
    } else {
      if (open) alerts.push(open);
      open = h.safetyFlag ? { message: h.safetyFlag, startMs, endMs: startMs + 3_600_000 } : null;
    }
  }
  if (open) alerts.push(open);
  return alerts;
}

function formatMs(ms: number) {
  return new Date(ms).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}

function AlertBanner({ children }: { children: React.ReactNode }) {
  return (
    <div className="bg-red-50 border border-red-200 rounded-2xl px-4 py-3 text-sm text-red-700">
      ⚠️ {children}
    </div>
  );
}

function WindowList({ title, windows }: { title: string; windows: TimeWindow[] }) {
  return (
    <div className="bg-white rounded-2xl border border-gray-100 p-4">
      <p className="text-sm font-semibold text-gray-700 mb-2">{title}</p>
      {windows.length === 0 ? (
        <p className="text-xs text-gray-400">None this day</p>
      ) : (
        <ul className="flex flex-col gap-1.5">
          {windows.map((w) => (
            <li key={w.start} className="text-sm text-blue-700 bg-blue-50 rounded-lg px-3 py-1.5">
              {formatWindow(w)}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export default function TimingPage() {
  const [coords, setCoords] = useState<{ lat: number; lon: number } | null>(null);
  const [geoError, setGeoError] = useState<string | null>(null);
  const [locationName, setLocationName] = useState<string | null>(null);
  const [forecast, setForecast] = useState<BiteForecast | null>(null);
  const [fetchError, setFetchError] = useState<string | null>(null);
  const [selectedDay, setSelectedDay] = useState<string | null>(null);
  const [factorTab, setFactorTab] = useState<FactorTab>("overall");
  const [calendarOpen, setCalendarOpen] = useState(false);

  function requestLocation() {
    setGeoError(null);
    if (!navigator.geolocation) {
      setGeoError("This browser does not support geolocation.");
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (pos) => setCoords({ lat: pos.coords.latitude, lon: pos.coords.longitude }),
      () => setGeoError("Location access is needed to forecast fish activity for your area."),
    );
  }

  useEffect(requestLocation, []);

  useEffect(() => {
    if (!coords) return;
    let cancelled = false;
    setForecast(null);
    setFetchError(null);
    api.biteScore
      .forecast(coords.lat, coords.lon)
      .then((f) => {
        if (cancelled) return;
        setForecast(f);
        setSelectedDay((d) => d ?? (f.hourly[0] ? isoDateOf(f.hourly[0].timestamp) : null));
      })
      .catch((e) => {
        if (!cancelled) setFetchError(e instanceof Error ? e.message : String(e));
      });
    fetch(`${REVERSE_GEOCODE_URL}?latitude=${coords.lat}&longitude=${coords.lon}&localityLanguage=en`)
      .then((r) => r.json())
      .then((g) => {
        if (!cancelled) setLocationName(g.city || g.locality || null);
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, [coords]);

  // 14 days of data: strip shows the first 7, the calendar popover all 14.
  const days = useMemo(() => {
    if (!forecast) return [];
    const seen: string[] = [];
    for (const h of forecast.hourly) {
      const d = isoDateOf(h.timestamp);
      if (!seen.includes(d)) seen.push(d);
      if (seen.length === 14) break;
    }
    return seen;
  }, [forecast]);
  const stripDays = days.slice(0, 7);

  const hoursByDay = useMemo(() => {
    const map = new Map<string, BiteHourlyScore[]>();
    for (const h of forecast?.hourly ?? []) {
      const d = isoDateOf(h.timestamp);
      map.set(d, [...(map.get(d) ?? []), h]);
    }
    return map;
  }, [forecast]);

  const valueOf = useMemo(
    () =>
      factorTab === "overall"
        ? (h: BiteHourlyScore) => h.score
        : (h: BiteHourlyScore) => h.breakdown[factorTab] ?? 0,
    [factorTab],
  );

  // Daily aggregate = mean over the 4:00-20:00 window; the calendar always
  // shows the overall score regardless of the active factor tab.
  const dailyScores = useMemo(() => {
    const map = new Map<string, number | null>();
    for (const d of days) map.set(d, dayWindowMean(hoursByDay.get(d) ?? [], (h) => h.score));
    return map;
  }, [days, hoursByDay]);

  const dayHours = selectedDay ? hoursByDay.get(selectedDay) ?? [] : [];
  const gaugeScore = dayWindowMean(dayHours, valueOf);
  const chartPoints: ChartPoint[] = dayHours.map((h) => ({
    hour: new Date(h.timestamp).getHours(),
    time: formatTime(h.timestamp),
    value: valueOf(h),
  }));

  const majorToday = selectedDay && forecast ? windowsForDay(forecast.majorWindows, selectedDay) : [];
  const minorToday = selectedDay && forecast ? windowsForDay(forecast.minorWindows, selectedDay) : [];
  const majorRanges = selectedDay ? majorToday.map((w) => toHourRange(w, selectedDay)) : [];
  const minorRanges = selectedDay ? minorToday.map((w) => toHourRange(w, selectedDay)) : [];

  const dayAlerts = safetyAlerts(dayHours);
  const current = forecast?.current;
  const nowAlert =
    current && (current.isStorm || current.isHeavyPrecip)
      ? current.isStorm
        ? "Storm at your location right now — do not fish through lightning."
        : "Heavy rain at your location right now — fishing is not recommended."
      : null;

  const sunToday = selectedDay ? forecast?.sunTimes.find((s) => s.date === selectedDay) : undefined;
  const sunMarks = sunToday
    ? [
        { x: hourOfDay(sunToday.sunrise, selectedDay!), label: `☀ ${formatTime(sunToday.sunrise)}` },
        { x: hourOfDay(sunToday.sunset, selectedDay!), label: `☀ ${formatTime(sunToday.sunset)}` },
      ].filter((m) => m.x >= 0 && m.x <= 24)
    : [];

  return (
    <main className="min-h-screen bg-gray-50 py-10">
      <div className="max-w-2xl mx-auto px-4 flex flex-col gap-5">
        <div className="flex items-start justify-between relative">
          <div>
            <h1 className="text-2xl font-bold text-gray-900">Fish activity</h1>
            <p className="text-sm text-blue-700 font-medium">
              {locationName ??
                (coords ? `${coords.lat.toFixed(2)}, ${coords.lon.toFixed(2)}` : "Locating…")}
            </p>
          </div>
          <button
            onClick={() => setCalendarOpen((o) => !o)}
            aria-label="Open 7-day calendar"
            className="p-2 rounded-xl bg-white border border-gray-100 text-blue-600 hover:bg-blue-50"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <rect x="3" y="4" width="18" height="18" rx="2" />
              <line x1="16" y1="2" x2="16" y2="6" />
              <line x1="8" y1="2" x2="8" y2="6" />
              <line x1="3" y1="10" x2="21" y2="10" />
            </svg>
          </button>
          {calendarOpen && selectedDay && (
            <CalendarPopover
              days={days}
              dailyScores={dailyScores}
              selected={selectedDay}
              onSelect={setSelectedDay}
              onClose={() => setCalendarOpen(false)}
            />
          )}
        </div>

        {geoError ? (
          <div className="bg-white rounded-2xl border border-gray-100 p-8 text-center flex flex-col items-center gap-3">
            <p className="text-sm text-gray-500">{geoError}</p>
            <button
              onClick={requestLocation}
              className="text-sm bg-blue-600 text-white px-4 py-2 rounded-lg hover:bg-blue-700"
            >
              Enable location
            </button>
          </div>
        ) : fetchError ? (
          <div className="bg-white rounded-2xl border border-gray-100 p-8 text-center text-sm text-gray-500">
            Forecast unavailable right now ({fetchError}). Try again shortly.
          </div>
        ) : !forecast || !selectedDay ? (
          <div className="bg-white rounded-2xl border border-gray-100 p-8 text-center text-sm text-gray-400 animate-pulse">
            Loading 7-day forecast…
          </div>
        ) : (
          <>
            {nowAlert && <AlertBanner>{nowAlert}</AlertBanner>}

            <DayStrip days={stripDays} dailyScores={dailyScores} selected={selectedDay} onSelect={setSelectedDay} />

            {dayAlerts.map((a) => (
              <AlertBanner key={a.startMs}>
                <span className="font-semibold">
                  {formatMs(a.startMs)}–{formatMs(a.endMs)}:
                </span>{" "}
                {a.message}
              </AlertBanner>
            ))}

            <div className="bg-white rounded-2xl border border-gray-100 p-5 flex flex-col gap-4">
              <p className="text-sm text-gray-500 text-center">
                {new Date(`${selectedDay}T12:00:00`).toLocaleDateString(undefined, {
                  weekday: "long",
                  month: "long",
                  day: "numeric",
                })}
              </p>
              <ActivityGauge score={gaugeScore} label={gaugeLabel(factorTab, gaugeScore)} />
              <ActivityChart
                points={chartPoints}
                majorRanges={majorRanges}
                minorRanges={minorRanges}
                sunMarks={sunMarks}
              />
              <div className="flex gap-1.5 flex-wrap justify-center">
                {(["overall", ...FACTORS] as FactorTab[]).map((tab) => (
                  <button
                    key={tab}
                    onClick={() => setFactorTab(tab)}
                    className={`text-xs px-3 py-1.5 rounded-full capitalize transition-colors ${
                      factorTab === tab
                        ? "bg-blue-600 text-white"
                        : "bg-blue-50 text-blue-700 hover:bg-blue-100"
                    }`}
                  >
                    {tab}
                  </button>
                ))}
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <WindowList title="Major times" windows={majorToday} />
              <WindowList title="Minor times" windows={minorToday} />
            </div>
          </>
        )}
      </div>
    </main>
  );
}
