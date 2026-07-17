"use client";

const RADIUS = 62;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

export function ActivityGauge({ score, label }: { score: number | null; label: string }) {
  const fraction = score === null ? 0 : Math.max(0, Math.min(100, score)) / 100;
  return (
    <div className="flex flex-col items-center gap-2">
      <svg width={160} height={160} viewBox="0 0 160 160">
        <circle cx={80} cy={80} r={RADIUS} fill="none" stroke="#dbeafe" strokeWidth={12} />
        <circle
          cx={80}
          cy={80}
          r={RADIUS}
          fill="none"
          stroke="#2563eb"
          strokeWidth={12}
          strokeLinecap="round"
          strokeDasharray={`${fraction * CIRCUMFERENCE} ${CIRCUMFERENCE}`}
          transform="rotate(-90 80 80)"
        />
        <text x={80} y={78} textAnchor="middle" className="fill-gray-900" fontSize={36} fontWeight={700}>
          {score === null ? "–" : Math.round(score)}
        </text>
        <text x={80} y={100} textAnchor="middle" className="fill-gray-400" fontSize={13}>
          / 100
        </text>
      </svg>
      <p className="text-sm font-medium text-blue-700">{label}</p>
    </div>
  );
}
