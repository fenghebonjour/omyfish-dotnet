"use client";

import { useEffect, useState } from "react";
import { api, BiteForecast, BiteHourlyScore } from "@/lib/api";

const FACTORS = ["pressure", "temperature", "wind", "water", "solunar", "sky"] as const;

function scoreColor(score: number) {
  if (score >= 70) return "text-green-600";
  if (score >= 40) return "text-yellow-600";
  return "text-red-500";
}

function barColor(score: number) {
  if (score >= 70) return "bg-green-500";
  if (score >= 40) return "bg-yellow-400";
  return "bg-red-400";
}

function windowLabel(w: BiteHourlyScore) {
  const d = new Date(w.timestamp);
  return d.toLocaleString(undefined, { weekday: "short", hour: "numeric" });
}

export function BiteScorePanel({
  lat,
  lon,
  species,
}: {
  lat: number;
  lon: number;
  species?: string;
}) {
  const [forecast, setForecast] = useState<BiteForecast | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    api.biteScore
      .today(lat, lon, species ?? "general")
      .then((f) => { if (!cancelled) setForecast(f); })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : String(e)); });
    return () => { cancelled = true; };
  }, [lat, lon, species]);

  if (error)
    return <div className="mt-3 text-xs text-gray-400">Bite score unavailable: {error}</div>;
  if (!forecast)
    return <div className="mt-3 text-xs text-gray-400 animate-pulse">Loading bite score…</div>;

  const now = forecast.hourly[0];
  if (!now)
    return <div className="mt-3 text-xs text-gray-400">No forecast data for this spot.</div>;

  return (
    <div className="mt-3 border-t pt-3 flex flex-col gap-3">
      {now.safetyFlag && (
        <div className="bg-red-50 border border-red-200 rounded-lg px-3 py-2 text-xs text-red-700">
          ⚠️ {now.safetyFlag}
        </div>
      )}

      <div className="flex items-center gap-4">
        <div>
          <span className={`text-3xl font-bold ${scoreColor(now.score)}`}>
            {Math.round(now.score)}
          </span>
          <span className="text-sm text-gray-400"> / 100</span>
        </div>
        <div className="text-xs text-gray-500">
          <p className="font-medium text-gray-700">Bite score now</p>
          <p>tuned for {forecast.species.replace(/_/g, " ")}</p>
        </div>
      </div>

      <div className="grid grid-cols-2 gap-x-6 gap-y-1.5">
        {FACTORS.map((f) => {
          const v = now.breakdown[f] ?? 0;
          return (
            <div key={f} className="flex items-center gap-2">
              <span className="text-xs text-gray-500 w-20 capitalize">{f}</span>
              <div className="flex-1 bg-gray-100 rounded-full h-1.5">
                <div className={`h-1.5 rounded-full ${barColor(v)}`} style={{ width: `${v}%` }} />
              </div>
              <span className="text-xs text-gray-400 w-7 text-right">{Math.round(v)}</span>
            </div>
          );
        })}
      </div>

      {forecast.bestWindows.length > 0 && (
        <div className="flex items-center gap-2 flex-wrap">
          <span className="text-xs text-gray-500">Best today:</span>
          {forecast.bestWindows.map((w) => (
            <span
              key={w.timestamp}
              className="text-xs bg-blue-50 text-blue-700 rounded-full px-2.5 py-0.5"
            >
              {windowLabel(w)} · {Math.round(w.score)}
            </span>
          ))}
        </div>
      )}
    </div>
  );
}
