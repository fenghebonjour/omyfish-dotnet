"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useAuth } from "@/contexts/AuthContext";
import { api, ObservationDto, RegsStation } from "@/lib/api";
import { ObservationMap } from "@/components/ObservationMap";
import { BiteScorePanel } from "@/components/BiteScorePanel";

export default function ObservationsPage() {
  const { isAuthenticated, isLoading: authLoading, token } = useAuth();
  const router = useRouter();
  const [observations, setObservations] = useState<ObservationDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showRegsLayer, setShowRegsLayer] = useState(false);
  const [zonesGeoJson, setZonesGeoJson] = useState<GeoJSON.FeatureCollection>();
  const [stations, setStations] = useState<RegsStation[]>();

  useEffect(() => {
    if (authLoading) return;
    if (!isAuthenticated) {
      router.push("/login");
      return;
    }
    api.observations
      .getAll(token!, true)
      .then(setObservations)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [isAuthenticated, authLoading, token, router]);

  const handleDelete = async (id: string) => {
    if (!confirm("Delete this observation?")) return;
    try {
      await api.observations.delete(id, token!);
      setObservations((prev) => prev.filter((o) => o.id !== id));
    } catch (e) {
      alert("Delete failed: " + (e instanceof Error ? e.message : e));
    }
  };

  const mapMarkers = observations
    .filter((o) => o.latitude != null && o.longitude != null)
    .map((o) => ({
      lat: o.latitude!,
      lng: o.longitude!,
      label: o.speciesName,
      imageUrl: o.imageUrl,
      date: new Date(o.observedAt).toLocaleDateString(),
    }));

  const toggleRegsLayer = async () => {
    const next = !showRegsLayer;
    setShowRegsLayer(next);
    if (next && !zonesGeoJson) {
      const first = mapMarkers[0];
      const [zones, nearbyStations] = await Promise.all([
        api.regs.zonesGeoJson(),
        first ? api.regs.consumptionStations(first.lat, first.lng) : Promise.resolve([]),
      ]);
      setZonesGeoJson(zones);
      setStations(nearbyStations);
    }
  };

  if (loading) return <div className="text-center py-16 text-gray-400">Loading observations…</div>;

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">My Observations</h1>
        <Link href="/" className="text-sm text-blue-600 hover:underline">
          + New identification
        </Link>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700 text-sm">{error}</div>
      )}

      <label className="flex items-center gap-2 text-sm text-gray-600">
        <input type="checkbox" checked={showRegsLayer} onChange={toggleRegsLayer} />
        Show Quebec fishing zones &amp; consumption-advisory stations
      </label>

      <ObservationMap
        markers={mapMarkers}
        height="300px"
        zonesGeoJson={showRegsLayer ? zonesGeoJson : undefined}
        stations={showRegsLayer ? stations : undefined}
      />

      {observations.length === 0 ? (
        <div className="text-center py-16 text-gray-400">
          <p className="text-lg">No observations yet.</p>
          <Link href="/" className="mt-2 inline-block text-blue-600 hover:underline text-sm">
            Identify a fish to get started
          </Link>
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {observations.map((obs) => (
            <ObservationCard key={obs.id} obs={obs} onDelete={handleDelete} />
          ))}
        </div>
      )}
    </div>
  );
}

function ObservationCard({
  obs,
  onDelete,
}: {
  obs: ObservationDto;
  onDelete: (id: string) => void;
}) {
  const [showBiteScore, setShowBiteScore] = useState(false);
  const confidence = Math.round(obs.topConfidence * 100);
  const barColor = confidence >= 85 ? "bg-green-500" : confidence >= 50 ? "bg-yellow-400" : "bg-red-400";
  const hasLocation = obs.latitude != null && obs.longitude != null;

  return (
    <div className="border rounded-xl p-4 bg-white shadow-sm flex gap-4">
      {obs.imageUrl && (
        <img
          src={obs.imageUrl}
          alt={obs.speciesName}
          className="w-20 h-20 object-cover rounded-lg flex-shrink-0"
          onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }}
        />
      )}
      <div className="flex-1 min-w-0">
        <div className="flex items-start justify-between gap-2">
          <div>
            <p className="font-semibold text-gray-900">{obs.speciesName}</p>
            {obs.scientificName && (
              <p className="text-sm text-gray-500 italic">{obs.scientificName}</p>
            )}
          </div>
          <button
            onClick={() => onDelete(obs.id)}
            className="text-gray-400 hover:text-red-500 transition-colors text-sm flex-shrink-0"
            title="Delete observation"
          >
            ✕
          </button>
        </div>

        <div className="mt-2 flex items-center gap-2">
          <div className="flex-1 bg-gray-100 rounded-full h-1.5 max-w-[120px]">
            <div className={`h-1.5 rounded-full ${barColor}`} style={{ width: `${confidence}%` }} />
          </div>
          <span className="text-xs text-gray-500">{confidence}% confidence</span>
        </div>

        <div className="mt-1 flex items-center gap-3 text-xs text-gray-400 flex-wrap">
          <span>{new Date(obs.observedAt).toLocaleDateString()}</span>
          {hasLocation && (
            <span>📍 {obs.latitude!.toFixed(4)}, {obs.longitude!.toFixed(4)}</span>
          )}
          {obs.notes && <span>📝 {obs.notes}</span>}
          {hasLocation && (
            <button
              onClick={() => setShowBiteScore((s) => !s)}
              className="text-blue-600 hover:underline"
            >
              🎣 {showBiteScore ? "Hide bite score" : "Bite score"}
            </button>
          )}
        </div>

        {showBiteScore && hasLocation && (
          <BiteScorePanel
            lat={obs.latitude!}
            lon={obs.longitude!}
            species={obs.speciesName}
          />
        )}
      </div>
    </div>
  );
}
