"use client";

import {
  Line,
  LineChart,
  ReferenceArea,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { BAND_THRESHOLDS } from "@/lib/biteScore";

export interface ChartPoint {
  hour: number; // 0..23
  time: string; // "4:00 AM"
  value: number; // 0..100
}

/** A major/minor peak window mapped onto the day's 0-24 hour axis. */
export interface HourRange {
  x1: number;
  x2: number;
}

function formatHour(hour: number) {
  if (hour === 0 || hour === 24) return "12 AM";
  if (hour === 12) return "12 PM";
  return hour < 12 ? `${hour} AM` : `${hour - 12} PM`;
}

export function ActivityChart({
  points,
  majorRanges,
  minorRanges,
  sunMarks,
}: {
  points: ChartPoint[];
  majorRanges: HourRange[];
  minorRanges: HourRange[];
  sunMarks: Array<{ x: number; label: string }>; // sunrise/sunset — the dawn/dusk boost
}) {
  if (points.length === 0) {
    return <p className="text-sm text-gray-400 py-10 text-center">No forecast data for this day.</p>;
  }

  return (
    <div className="h-56 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <LineChart data={points} margin={{ top: 18, right: 12, bottom: 0, left: 0 }}>
          {/* Low / Medium / High horizontal bands */}
          <ReferenceArea y1={0} y2={BAND_THRESHOLDS.medium} fill="#f8fafc" fillOpacity={1} />
          <ReferenceArea y1={BAND_THRESHOLDS.medium} y2={BAND_THRESHOLDS.high} fill="#eff6ff" fillOpacity={1} />
          <ReferenceArea y1={BAND_THRESHOLDS.high} y2={100} fill="#dbeafe" fillOpacity={0.7} />
          {/* Score-peak windows — matches the Major/Minor times lists below the chart */}
          {minorRanges.map((r) => (
            <ReferenceArea key={`minor-${r.x1}`} x1={r.x1} x2={r.x2} fill="#60a5fa" fillOpacity={0.12} />
          ))}
          {majorRanges.map((r) => (
            <ReferenceArea key={`major-${r.x1}`} x1={r.x1} x2={r.x2} fill="#2563eb" fillOpacity={0.18} />
          ))}
          {/* Sunrise/sunset — the dawn/dusk peaks come from these, not the moon */}
          {sunMarks.map((m) => (
            <ReferenceLine
              key={`sun-${m.x}`}
              x={m.x}
              stroke="#f59e0b"
              strokeDasharray="3 3"
              label={{ value: m.label, position: "top", fontSize: 10, fill: "#d97706" }}
            />
          ))}
          <XAxis
            dataKey="hour"
            type="number"
            domain={[0, 24]}
            ticks={[0, 4, 8, 12, 16, 20, 24]}
            tickFormatter={formatHour}
            tickLine={false}
            axisLine={{ stroke: "#e5e7eb" }}
            tick={{ fontSize: 11, fill: "#9ca3af" }}
          />
          <YAxis
            domain={[0, 100]}
            ticks={[20, 55, 85]}
            tickFormatter={(v: number) => (v >= 85 ? "High" : v >= 55 ? "Medium" : "Low")}
            tickLine={false}
            axisLine={false}
            width={52}
            tick={{ fontSize: 11, fill: "#9ca3af" }}
          />
          <Tooltip
            cursor={{ stroke: "#2563eb", strokeWidth: 1, strokeDasharray: "4 2" }}
            content={({ active, payload }) => {
              if (!active || !payload?.length) return null;
              const p = payload[0].payload as ChartPoint;
              return (
                <div className="bg-white border border-blue-200 rounded-lg shadow-sm px-3 py-1.5 text-xs">
                  <span className="font-medium text-gray-700">{p.time}</span>
                  <span className="text-blue-600 font-semibold ml-2">{Math.round(p.value)}</span>
                </div>
              );
            }}
          />
          <Line
            type="monotone"
            dataKey="value"
            stroke="#2563eb"
            strokeWidth={2.5}
            dot={false}
            activeDot={{ r: 5, fill: "#2563eb", stroke: "#fff", strokeWidth: 2 }}
            isAnimationActive={false}
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
}
