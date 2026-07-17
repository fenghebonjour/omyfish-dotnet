"use client";

export function DayStrip({
  days,
  dailyScores,
  selected,
  onSelect,
}: {
  days: string[]; // ISO dates, first is today
  dailyScores: Map<string, number | null>;
  selected: string;
  onSelect: (day: string) => void;
}) {
  return (
    <div className="flex gap-1.5">
      {days.map((day, i) => {
        const date = new Date(`${day}T12:00:00`);
        const score = dailyScores.get(day) ?? null;
        const isSelected = day === selected;
        return (
          <button
            key={day}
            onClick={() => onSelect(day)}
            className={`flex-1 flex flex-col items-center rounded-xl py-2 text-sm transition-colors ${
              isSelected
                ? "bg-blue-600 text-white"
                : "bg-white text-gray-600 hover:bg-blue-50 border border-gray-100"
            }`}
          >
            <span className={`text-xs ${isSelected ? "text-blue-100" : "text-gray-400"}`}>
              {i === 0 ? "Today" : date.toLocaleDateString(undefined, { weekday: "short" })}
            </span>
            <span className="font-semibold">{date.getDate()}</span>
            <span className={`text-[11px] font-medium ${isSelected ? "text-white" : "text-blue-600"}`}>
              {score === null ? "–" : `${Math.round(score)}%`}
            </span>
          </button>
        );
      })}
    </div>
  );
}
