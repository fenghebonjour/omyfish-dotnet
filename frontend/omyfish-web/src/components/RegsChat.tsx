"use client";

import { useState } from "react";
import { api } from "@/lib/api";

interface ChatMessage {
  role: "user" | "assistant";
  text: string;
  sources?: string[];
}

export function RegsChat() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [input, setInput] = useState("");
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const send = async () => {
    const question = input.trim();
    if (!question || sending) return;
    setInput("");
    setError(null);
    setMessages((prev) => [...prev, { role: "user", text: question }]);
    setSending(true);
    try {
      const response = await api.regs.ask(question);
      setMessages((prev) => [
        ...prev,
        { role: "assistant", text: response.answer, sources: response.sources },
      ]);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to get an answer");
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="flex flex-col gap-4 max-w-2xl mx-auto p-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Regs &amp; Tips</h1>
        <p className="text-xs text-gray-500 mt-1">
          Informational only — verify current regulations at quebec.ca before fishing.
        </p>
      </div>

      <div className="flex flex-col gap-3 min-h-[300px]">
        {messages.length === 0 && (
          <p className="text-sm text-gray-400 text-center py-8">
            Ask about Quebec fishing regulations, tackle, or techniques.
          </p>
        )}
        {messages.map((m, i) => (
          <div key={i} className={`flex ${m.role === "user" ? "justify-end" : "justify-start"}`}>
            <div
              className={`max-w-[80%] rounded-xl px-3 py-2 text-sm ${
                m.role === "user"
                  ? "bg-blue-600 text-white"
                  : "bg-gray-100 text-gray-900"
              }`}
            >
              <p>{m.text}</p>
              {m.sources && m.sources.length > 0 && (
                <p className="mt-1 text-xs opacity-70">Sources: {m.sources.join(", ")}</p>
              )}
            </div>
          </div>
        ))}
        {sending && <p className="text-sm text-gray-400">Thinking…</p>}
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-3 text-red-700 text-sm">
          {error}
        </div>
      )}

      <div className="flex gap-2">
        <input
          type="text"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Enter") send(); }}
          placeholder="Ask a question…"
          className="flex-1 border rounded-lg px-3 py-2 text-sm text-gray-900"
        />
        <button
          onClick={send}
          disabled={sending || !input.trim()}
          className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium disabled:opacity-50 hover:bg-blue-700 transition-colors"
        >
          Send
        </button>
      </div>
    </div>
  );
}
