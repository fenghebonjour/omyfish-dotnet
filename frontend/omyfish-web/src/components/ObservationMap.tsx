"use client";

import { useEffect, useRef } from "react";

interface MapMarker {
  lat: number;
  lng: number;
  label: string;
}

interface ObservationMapProps {
  markers: MapMarker[];
  height?: string;
}

export function ObservationMap({ markers, height = "400px" }: ObservationMapProps) {
  const mapRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!mapRef.current || markers.length === 0) return;

    const center = markers[0];
    const el = mapRef.current;

    // Static placeholder — replace with Leaflet or MapLibre when tile provider is configured
    el.innerHTML = `
      <div style="
        display:flex; align-items:center; justify-content:center;
        height:100%; background:#e8f4fd; border-radius:8px;
        color:#2563eb; font-size:14px; flex-direction:column; gap:8px;
      ">
        <span style="font-size:24px">🗺️</span>
        <span>${markers.length} observation${markers.length !== 1 ? "s" : ""}</span>
        <span style="color:#6b7280; font-size:12px">
          Center: ${center.lat.toFixed(4)}, ${center.lng.toFixed(4)}
        </span>
      </div>
    `;
  }, [markers]);

  if (markers.length === 0) return null;

  return <div ref={mapRef} style={{ height, borderRadius: 8, overflow: "hidden" }} />;
}
