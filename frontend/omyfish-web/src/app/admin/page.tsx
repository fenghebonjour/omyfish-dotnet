"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/contexts/AuthContext";
import { api, AdminStats, AdminSubscriptionRow } from "@/lib/api";

const STATUS_STYLES: Record<string, string> = {
  active: "bg-green-50 text-green-700 border-green-200",
  trialing: "bg-blue-50 text-blue-700 border-blue-200",
  expired: "bg-amber-50 text-amber-700 border-amber-200",
  canceled: "bg-gray-100 text-gray-500 border-gray-200",
};

export default function AdminPage() {
  const { isAuthenticated, isLoading: authLoading, token } = useAuth();
  const router = useRouter();
  const [stats, setStats] = useState<AdminStats | null>(null);
  const [subs, setSubs] = useState<AdminSubscriptionRow[]>([]);
  const [forbidden, setForbidden] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(() => {
    Promise.all([api.admin.stats(token!), api.admin.subscriptions(token!)])
      .then(([s, rows]) => {
        setStats(s);
        setSubs(rows);
      })
      .catch((e) => {
        if (String(e).startsWith("403")) setForbidden(true);
        else setError(String(e));
      });
  }, [token]);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      router.push("/login");
      return;
    }
    load();
  }, [isAuthenticated, authLoading, router, load]);

  async function act(fn: () => Promise<unknown>) {
    setError(null);
    try {
      await fn();
      load();
    } catch (e) {
      setError(String(e));
    }
  }

  if (authLoading || (!stats && !forbidden && !error)) {
    return (
      <main className="min-h-screen bg-gray-50 flex items-center justify-center">
        <p className="text-gray-400 animate-pulse">Loading admin...</p>
      </main>
    );
  }

  if (forbidden) {
    return (
      <main className="min-h-screen bg-gray-50 flex items-center justify-center">
        <p className="text-gray-500">This page is for administrators only.</p>
      </main>
    );
  }

  return (
    <main className="min-h-screen bg-gray-50 py-10">
      <div className="max-w-5xl mx-auto px-4 flex flex-col gap-6">
        <h1 className="text-2xl font-bold text-gray-900">Admin</h1>

        {error && (
          <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700 text-sm">{error}</div>
        )}

        {stats && (
          <div className="grid grid-cols-2 sm:grid-cols-5 gap-3">
            {[
              ["Users", stats.users],
              ["Active subs", stats.subscriptions["active"] ?? 0],
              ["On trial", stats.subscriptions["trialing"] ?? 0],
              ["Expired", stats.subscriptions["expired"] ?? 0],
              ["MRR (CAD)", `$${stats.mrrCad.toFixed(2)}`],
            ].map(([label, value]) => (
              <div key={label} className="bg-white border border-gray-200 rounded-xl p-4 shadow-sm">
                <p className="text-xs text-gray-400">{label}</p>
                <p className="text-xl font-semibold text-gray-900 mt-1">{value}</p>
              </div>
            ))}
          </div>
        )}

        <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-x-auto">
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-gray-400 border-b border-gray-100">
                <th className="p-3">Email</th>
                <th className="p-3">Status</th>
                <th className="p-3">Plan</th>
                <th className="p-3">Trial ends</th>
                <th className="p-3">Period ends</th>
                <th className="p-3 text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              {subs.map((s) => (
                <tr key={s.userId} className="border-b border-gray-50 last:border-0">
                  <td className="p-3 text-gray-900">{s.email}</td>
                  <td className="p-3">
                    <span className={`text-xs border rounded-full px-2 py-0.5 ${STATUS_STYLES[s.status] ?? ""}`}>
                      {s.status}
                    </span>
                  </td>
                  <td className="p-3 text-gray-500">{s.plan ?? "—"}</td>
                  <td className="p-3 text-gray-500">
                    {s.trialEnd ? new Date(s.trialEnd).toLocaleDateString() : "—"}
                  </td>
                  <td className="p-3 text-gray-500">
                    {s.currentPeriodEnd ? new Date(s.currentPeriodEnd).toLocaleDateString() : "—"}
                  </td>
                  <td className="p-3 text-right whitespace-nowrap">
                    <button
                      onClick={() => act(() => api.admin.grant(s.userId, token!))}
                      className="text-xs text-blue-600 hover:underline mr-3"
                    >
                      Grant 1y
                    </button>
                    <button
                      onClick={() => act(() => api.admin.extendTrial(s.userId, token!))}
                      className="text-xs text-blue-600 hover:underline mr-3"
                    >
                      +7d trial
                    </button>
                    <button
                      onClick={() => act(() => api.admin.revoke(s.userId, token!))}
                      className="text-xs text-red-500 hover:underline"
                    >
                      Revoke
                    </button>
                  </td>
                </tr>
              ))}
              {subs.length === 0 && (
                <tr>
                  <td colSpan={6} className="p-6 text-center text-gray-400">
                    No subscriptions yet
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </main>
  );
}
