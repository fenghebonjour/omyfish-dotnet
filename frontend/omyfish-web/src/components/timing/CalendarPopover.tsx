"use client";

export function CalendarPopover({
  days,
  dailyScores,
  selected,
  onSelect,
  onClose,
}: {
  days: string[]; // ISO dates — 14-day window (the strip shows the first 7)
  dailyScores: Map<string, number | null>;
  selected: string;
  onSelect: (day: string) => void;
  onClose: () => void;
}) {
  return (
    <>
      <div className="fixed inset-0 z-10" onClick={onClose} />
      <div className="absolute right-0 top-10 z-20 bg-white border border-blue-100 rounded-2xl shadow-lg p-4 w-[26rem] max-w-[90vw]">
        <p className="text-sm font-semibold text-gray-700 mb-3">Next 14 days</p>
        <div className="grid grid-cols-7 gap-1.5">
          {days.map((day) => {
            const date = new Date(`${day}T12:00:00`);
            const score = dailyScores.get(day) ?? null;
            const isSelected = day === selected;
            return (
              <button
                key={day}
                onClick={() => {
                  onSelect(day);
                  onClose();
                }}
                className={`flex flex-col items-center rounded-xl py-2 ${
                  isSelected ? "bg-blue-600 text-white" : "bg-blue-50/60 text-gray-700 hover:bg-blue-100"
                }`}
              >
                <span className={`text-[10px] ${isSelected ? "text-blue-100" : "text-gray-400"}`}>
                  {date.toLocaleDateString(undefined, { weekday: "short" })}
                </span>
                <span className="text-sm font-semibold">{date.getDate()}</span>
                <span className={`text-[11px] font-medium ${isSelected ? "text-white" : "text-blue-600"}`}>
                  {score === null ? "–" : `${Math.round(score)}%`}
                </span>
              </button>
            );
          })}
        </div>
      </div>
    </>
  );
}
