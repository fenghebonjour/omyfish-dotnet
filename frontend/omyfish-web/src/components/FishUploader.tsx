"use client";

import { useState, useCallback } from "react";
import { useDropzone } from "react-dropzone";
import { useAuth } from "@/contexts/AuthContext";
import exifr from "exifr";

interface PredictionDto {
  speciesName: string;
  scientificName: string;
  confidence: number;
  confidencePercent: string;
  rank: number;
  conservationStatus?: string;
  habitat?: string;
  diet?: string;
  maxSizeCm?: number;
  description?: string;
  funFact?: string;
}

interface IdentifyFishResult {
  predictions: PredictionDto[];
  uncertain: boolean;
  imageKey: string;
  isFish?: boolean;
}

export function FishUploader() {
  const { token, isAuthenticated } = useAuth();
  const [preview, setPreview] = useState<string | null>(null);
  const [currentFile, setCurrentFile] = useState<File | null>(null);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<IdentifyFishResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [saving, setSaving] = useState(false);
  const [lat, setLat] = useState("");
  const [lng, setLng] = useState("");

  const onDrop = useCallback(async (acceptedFiles: File[]) => {
    const file = acceptedFiles[0];
    if (!file) return;

    setCurrentFile(file);
    setPreview(URL.createObjectURL(file));
    setResult(null);
    setError(null);
    setSaved(false);
    setLat("");
    setLng("");
    setLoading(true);

    try {
      const formData = new FormData();
      formData.append("image", file);
      formData.append("topK", "5");

      const headers: HeadersInit = {};
      if (token) headers["Authorization"] = `Bearer ${token}`;

      const res = await fetch(
        `${process.env.NEXT_PUBLIC_API_URL}/api/v1/species/identify`,
        { method: "POST", body: formData, headers }
      );

      if (!res.ok) throw new Error(`HTTP ${res.status}: ${res.statusText}`);
      const data: IdentifyFishResult = await res.json();
      setResult(data);

      const fallbackToGeolocation = () => {
        navigator.geolocation?.getCurrentPosition(
          (pos) => {
            setLat(String(pos.coords.latitude));
            setLng(String(pos.coords.longitude));
          },
          () => {}
        );
      };

      try {
        const parsed = await exifr.parse(file, {
          gps: true,
          tiff: true,
          xmp: false,
          iptc: false,
          icc: false,
          jfif: false,
          ihdr: false,
          translateValues: true,
          reviveValues: true,
        });
        const exifLat = parsed?.latitude;
        const exifLng = parsed?.longitude;
        if (exifLat != null && exifLng != null && !isNaN(exifLat) && !isNaN(exifLng)) {
          setLat(String(exifLat));
          setLng(String(exifLng));
        } else {
          fallbackToGeolocation();
        }
      } catch {
        fallbackToGeolocation();
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Identification failed");
    } finally {
      setLoading(false);
    }
  }, [token]);

  const handleSave = async () => {
    if (!result || !currentFile || !token) return;
    setSaving(true);
    try {
      const top = result.predictions[0];
      const formData = new FormData();
      formData.append("image", currentFile);
      formData.append("speciesName", top.speciesName);
      formData.append("scientificName", top.scientificName ?? "");
      formData.append("topConfidence", String(top.confidence));

      if (lat) formData.append("latitude", lat);
      if (lng) formData.append("longitude", lng);

      const res = await fetch(
        `${process.env.NEXT_PUBLIC_API_URL}/api/v1/observations`,
        {
          method: "POST",
          body: formData,
          headers: { Authorization: `Bearer ${token}` },
        }
      );
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      setSaved(true);
    } catch (err) {
      alert("Save failed: " + (err instanceof Error ? err.message : err));
    } finally {
      setSaving(false);
    }
  };

  const { getRootProps, getInputProps, isDragActive } = useDropzone({
    onDrop,
    accept: { "image/*": [".jpg", ".jpeg", ".png", ".webp"] },
    maxSize: 20 * 1024 * 1024,
    multiple: false,
  });

  return (
    <div className="flex flex-col gap-6 max-w-2xl mx-auto p-6">
      <div
        {...getRootProps()}
        className={`border-2 border-dashed rounded-xl p-10 text-center cursor-pointer transition-colors
          ${isDragActive ? "border-blue-500 bg-blue-50" : "border-gray-300 hover:border-blue-400"}`}
      >
        <input {...getInputProps()} />
        {preview ? (
          <img
            src={preview}
            alt="Upload preview"
            className="max-h-64 mx-auto rounded-lg object-contain"
          />
        ) : (
          <p className="text-gray-500 text-lg">
            {isDragActive
              ? "Drop your fish photo here"
              : "Drag & drop a fish photo, or click to select"}
          </p>
        )}
      </div>

      {loading && (
        <div className="text-center text-blue-600 font-medium animate-pulse">
          Identifying species…
        </div>
      )}

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700">
          {error}
        </div>
      )}

      {result && result.isFish === false && (
        <div className="bg-orange-50 border border-orange-300 rounded-lg p-4 text-orange-800 text-center">
          🐟 That doesn&apos;t look like a fish. Try uploading a clear photo of a fish.
        </div>
      )}

      {result && result.isFish !== false && (
        <div className="flex flex-col gap-3">
          {result.uncertain && (
            <div className="bg-yellow-50 border border-yellow-300 rounded-lg p-3 text-yellow-800 text-sm">
              Low confidence — try a clearer photo in better lighting
            </div>
          )}
          {result.predictions.map((p) => (
            <PredictionCard key={p.rank} prediction={p} />
          ))}

          {isAuthenticated && !saved && (
            <div className="flex flex-col gap-2">
              <div className="flex gap-2 items-center">
                <label className="text-xs text-gray-500 w-20 shrink-0">Location</label>
                <input
                  type="number"
                  placeholder="Latitude"
                  value={lat}
                  onChange={(e) => setLat(e.target.value)}
                  className="flex-1 text-xs border rounded px-2 py-1 text-gray-700"
                  step="any"
                />
                <input
                  type="number"
                  placeholder="Longitude"
                  value={lng}
                  onChange={(e) => setLng(e.target.value)}
                  className="flex-1 text-xs border rounded px-2 py-1 text-gray-700"
                  step="any"
                />
              </div>
              <button
                onClick={handleSave}
                disabled={saving}
                className="w-full py-2 rounded-lg border border-blue-600 text-blue-600 hover:bg-blue-50 text-sm font-medium disabled:opacity-50 transition-colors"
              >
                {saving ? "Saving…" : "Save to My Observations"}
              </button>
            </div>
          )}
          {saved && (
            <div className="text-center text-sm text-green-600 font-medium">
              Saved to your observations
            </div>
          )}
          {!isAuthenticated && (
            <p className="text-center text-sm text-gray-400">
              <a href="/login" className="text-blue-600 hover:underline">Log in</a> to save this observation
            </p>
          )}
        </div>
      )}
    </div>
  );
}

const MEDALS = ["🥇", "🥈", "🥉"];

function conservationIcon(status: string): string {
  if (/Endangered/i.test(status)) return "🔴";
  if (/Vulnerable|Threatened/i.test(status)) return "🟡";
  return "🟢";
}

function PredictionCard({ prediction }: { prediction: PredictionDto }) {
  const pct = Math.round(prediction.confidence * 100);
  const barColor =
    pct >= 85 ? "bg-green-500" : pct >= 50 ? "bg-yellow-400" : "bg-red-400";
  const medal = MEDALS[prediction.rank - 1] ?? "";

  return (
    <div className="border rounded-xl p-4 flex flex-col gap-2 shadow-sm">
      <div className="flex justify-between items-start">
        <div>
          <p className="font-semibold text-gray-900">
            {medal && <span className="mr-1">{medal}</span>}
            {prediction.speciesName}
          </p>
          <p className="text-sm text-gray-500 italic">{prediction.scientificName}</p>
        </div>
        <span className="text-sm font-medium text-gray-700">
          {prediction.confidencePercent}
        </span>
      </div>
      <div className="w-full bg-gray-100 rounded-full h-2">
        <div
          className={`h-2 rounded-full ${barColor}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <div className="flex flex-col gap-1 text-sm text-gray-700">
        {prediction.habitat && (
          <p><span className="font-medium">Habitat:</span> {prediction.habitat}</p>
        )}
        {prediction.diet && (
          <p><span className="font-medium">Diet:</span> {prediction.diet}</p>
        )}
        {prediction.maxSizeCm != null && (
          <p><span className="font-medium">Max size:</span> {prediction.maxSizeCm} cm</p>
        )}
        {prediction.conservationStatus && (
          <p>
            <span className="font-medium">Conservation:</span>{" "}
            {conservationIcon(prediction.conservationStatus)} {prediction.conservationStatus}
          </p>
        )}
      </div>
      {prediction.description && (
        <p className="text-sm text-gray-600">{prediction.description}</p>
      )}
      {prediction.funFact && (
        <p className="text-sm bg-blue-50 border border-blue-100 rounded-lg p-2 text-blue-800">
          💡 {prediction.funFact}
        </p>
      )}
    </div>
  );
}
