"use client";

import Link from "next/link";
import { useAuth } from "@/contexts/AuthContext";
import { useRouter } from "next/navigation";

export function NavBar() {
  const { isAuthenticated, email, logout } = useAuth();
  const router = useRouter();

  const handleLogout = () => {
    logout();
    router.push("/");
  };

  return (
    <header className="bg-white border-b shadow-sm">
      <div className="max-w-5xl mx-auto px-4 py-3 flex items-center gap-4">
        <Link href="/" className="flex items-center gap-2 text-xl font-bold text-blue-700">
          <span>🐟</span>
          <span>OMyFish</span>
        </Link>

        <nav className="flex items-center gap-4 ml-4 flex-1">
          <Link href="/" className="text-sm text-gray-600 hover:text-blue-700 transition-colors">
            Identify
          </Link>
          <Link href="/timing" className="text-sm text-gray-600 hover:text-blue-700 transition-colors">
            Timing
          </Link>
          {isAuthenticated && (
            <Link href="/observations" className="text-sm text-gray-600 hover:text-blue-700 transition-colors">
              My Observations
            </Link>
          )}
        </nav>

        <div className="flex items-center gap-3">
          {isAuthenticated ? (
            <>
              <span className="text-sm text-gray-500 hidden sm:block">{email}</span>
              <button
                onClick={handleLogout}
                className="text-sm px-3 py-1.5 rounded-lg border border-gray-300 hover:bg-gray-50 transition-colors"
              >
                Log out
              </button>
            </>
          ) : (
            <>
              <Link
                href="/login"
                className="text-sm text-gray-600 hover:text-blue-700 transition-colors"
              >
                Log in
              </Link>
              <Link
                href="/register"
                className="text-sm px-3 py-1.5 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition-colors"
              >
                Sign up
              </Link>
            </>
          )}
        </div>
      </div>
    </header>
  );
}
