"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { useAuth } from "@/contexts/AuthContext";
import { api, SubscriptionDto } from "@/lib/api";

const PLAN_LABELS: Record<string, string> = {
  monthly: "5 CAD / month",
  yearly: "29 CAD / year",
};

export default function AccountPage() {
  const { isAuthenticated, isLoading: authLoading, token, email } = useAuth();
  const router = useRouter();
  const [sub, setSub] = useState<SubscriptionDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      router.push("/login");
      return;
    }
    api.billing.me(token!).then(setSub).catch((e) => setError(e.message));
  }, [isAuthenticated, authLoading, token, router]);

  async function subscribe(plan: "monthly" | "yearly") {
    setBusy(true);
    setError(null);
    try {
      const { checkoutUrl } = await api.billing.checkout(plan, token!);
      window.location.href = checkoutUrl;
    } catch (e) {
      setError(
        String(e).includes("503")
          ? "Payments are not configured on this deployment."
          : String(e)
      );
      setBusy(false);
    }
  }

  if (authLoading || (!sub && !error)) {
    return (
      <main className="min-h-screen bg-gray-50 flex items-center justify-center">
        <p className="text-gray-400 animate-pulse">Loading account...</p>
      </main>
    );
  }

  const trialDaysLeft = sub?.trialEnd
    ? Math.max(0, Math.ceil((new Date(sub.trialEnd).getTime() - Date.now()) / 86_400_000))
    : 0;

  return (
    <main className="min-h-screen bg-gray-50 py-10">
      <div className="max-w-2xl mx-auto px-4 flex flex-col gap-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Account</h1>
          <p className="text-sm text-gray-500 mt-1">{email}</p>
        </div>

        {error && (
          <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700 text-sm">{error}</div>
        )}

        {sub && (
          <div className="bg-white border border-gray-200 rounded-xl p-6 shadow-sm flex flex-col gap-4">
            <h2 className="font-semibold text-gray-900">Subscription</h2>

            {sub.status === "trialing" && (
              <p className="text-sm text-blue-700 bg-blue-50 border border-blue-200 rounded-lg p-3">
                Free trial — {trialDaysLeft} day{trialDaysLeft === 1 ? "" : "s"} left
              </p>
            )}
            {sub.status === "active" && (
              <p className="text-sm text-green-700 bg-green-50 border border-green-200 rounded-lg p-3">
                Subscribed{sub.plan ? ` — ${PLAN_LABELS[sub.plan] ?? sub.plan}` : ""}
                {sub.currentPeriodEnd &&
                  ` · renews ${new Date(sub.currentPeriodEnd).toLocaleDateString()}`}
              </p>
            )}
            {(sub.status === "expired" || sub.status === "canceled") && (
              <p className="text-sm text-amber-700 bg-amber-50 border border-amber-200 rounded-lg p-3">
                {sub.status === "expired"
                  ? "Your trial has ended — subscribe to keep full access."
                  : "Your subscription is canceled."}
              </p>
            )}

            {sub.status !== "active" && (
              <div className="grid grid-cols-2 gap-3">
                {(["monthly", "yearly"] as const).map((plan) => (
                  <button
                    key={plan}
                    onClick={() => subscribe(plan)}
                    disabled={busy}
                    className="border border-blue-600 text-blue-700 hover:bg-blue-50 disabled:opacity-50 rounded-lg py-3 text-sm font-medium"
                  >
                    {PLAN_LABELS[plan]}
                    {plan === "yearly" && (
                      <span className="block text-xs text-gray-400">2 months free</span>
                    )}
                  </button>
                ))}
              </div>
            )}
          </div>
        )}
      </div>
    </main>
  );
}
