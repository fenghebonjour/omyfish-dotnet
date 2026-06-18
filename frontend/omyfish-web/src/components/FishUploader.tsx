"use client";

import { useState, useCallback } from "react";
import { useDropzone } from "react-dropzone";

interface PredictionDto {
  speciesName: string;
  scientificName: string;
  confidence: number;
  confidencePercent: string;
  rank: number;
  conservationStatus?: string;
}

interface IdentifyFishResult {
  predictions: PredictionDto[];
  uncertain: boolean;
  imageKey: string;
}

export function FishUploader() {
  const [preview, setPreview] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState<IdentifyFishResult | null>(null);
  const [error, setError] = useState<string | null>(null);

  const onDrop = useCallback(async (acceptedFiles: File[]) => {
    const file = acceptedFiles[0];
    if (!file) return;

    setPreview(URL.createObjectURL(file));
    setResult(null);
    setError(null);
    setLoading(true);

    try {
      const formData = new FormData();
      formData.append("image", file);
      formData.append("topK", "5");

      const res = await fetch(
        `${process.env.NEXT_PUBLIC_API_URL}/api/v1/species/identify`,
        { method: "POST", body: formData }
      );

      if (!res.ok) throw new Error(`HTTP ${res.status}: ${res.statusText}`);
      const data: IdentifyFishResult = await res.json();
      setResult(data);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Identification failed");
    } finally {
      setLoading(false);
    }
  }, []);

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
          Identifying species...
        </div>
      )}

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700">
          {error}
        </div>
      )}

      {result && (
        <div className="flex flex-col gap-3">
          {result.uncertain && (
            <div className="bg-yellow-50 border border-yellow-300 rounded-lg p-3 text-yellow-800 text-sm">
              Low confidence — try a clearer photo in better lighting
            </div>
          )}
          {result.predictions.map((p) => (
            <PredictionCard key={p.rank} prediction={p} />
          ))}
        </div>
      )}
    </div>
  );
}

function PredictionCard({ prediction }: { prediction: PredictionDto }) {
  const pct = Math.round(prediction.confidence * 100);
  const barColor =
    pct >= 85 ? "bg-green-500" : pct >= 50 ? "bg-yellow-400" : "bg-red-400";

  return (
    <div className="border rounded-xl p-4 flex flex-col gap-2 shadow-sm">
      <div className="flex justify-between items-start">
        <div>
          <p className="font-semibold text-gray-900">{prediction.speciesName}</p>
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
      {prediction.conservationStatus && (
        <ConservationBadge status={prediction.conservationStatus} />
      )}
    </div>
  );
}

function ConservationBadge({ status }: { status: string }) {
  const colors: Record<string, string> = {
    LC: "bg-green-100 text-green-800",
    NT: "bg-lime-100 text-lime-800",
    VU: "bg-yellow-100 text-yellow-800",
    EN: "bg-orange-100 text-orange-800",
    CR: "bg-red-100 text-red-800",
    EW: "bg-purple-100 text-purple-800",
    EX: "bg-gray-200 text-gray-700",
  };
  return (
    <span
      className={`self-start text-xs px-2 py-0.5 rounded-full font-medium ${
        colors[status] ?? "bg-gray-100 text-gray-600"
      }`}
    >
      IUCN: {status}
    </span>
  );
}
