"use client";

import { useEffect, useRef } from "react";

interface MapMarker {
  lat: number;
  lng: number;
  label: string;
  imageUrl?: string;
  date?: string;
}

interface ObservationMapProps {
  markers: MapMarker[];
  height?: string;
}

function buildPopup(m: MapMarker): HTMLElement {
  const el = document.createElement("div");
  el.style.cssText = "text-align:center;min-width:160px";

  if (m.imageUrl) {
    const img = document.createElement("img");
    img.src = m.imageUrl;
    img.style.cssText = "width:160px;height:110px;object-fit:cover;border-radius:6px;display:block;margin-bottom:6px";
    el.appendChild(img);
  }

  const name = document.createElement("div");
  name.style.cssText = "font-weight:600;font-size:13px;color:#111";
  name.textContent = m.label;
  el.appendChild(name);

  if (m.date) {
    const dateEl = document.createElement("div");
    dateEl.style.cssText = "color:#888;font-size:11px;margin-top:2px";
    dateEl.textContent = m.date;
    el.appendChild(dateEl);
  }

  return el;
}

export function ObservationMap({ markers, height = "300px" }: ObservationMapProps) {
  const mapRef = useRef<HTMLDivElement>(null);
  const mapInstanceRef = useRef<unknown>(null);

  useEffect(() => {
    if (!mapRef.current || mapInstanceRef.current) return;

    // Dynamically import Leaflet to avoid SSR issues
    import("leaflet").then((L) => {
      // @ts-expect-error _getIconUrl is internal
      delete L.Icon.Default.prototype._getIconUrl;
      L.Icon.Default.mergeOptions({
        iconRetinaUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",
        iconUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
        shadowUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
        iconSize: [25, 41],
        iconAnchor: [12, 41],
        popupAnchor: [1, -34],
        shadowSize: [41, 41],
        shadowAnchor: [12, 41],
      });

      const center: [number, number] = markers.length > 0
        ? [markers[0].lat, markers[0].lng]
        : [20, 0];
      const zoom = markers.length > 0 ? 5 : 2;

      const map = L.map(mapRef.current!).setView(center, zoom);
      mapInstanceRef.current = map;

      L.tileLayer("https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png", {
        attribution: '© <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
        maxZoom: 18,
      }).addTo(map);

      markers.forEach((m) => {
        L.marker([m.lat, m.lng]).addTo(map).bindPopup(buildPopup(m));
      });
    });

    return () => {
      if (mapInstanceRef.current) {
        (mapInstanceRef.current as { remove: () => void }).remove();
        mapInstanceRef.current = null;
      }
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Update markers when they change
  useEffect(() => {
    if (!mapInstanceRef.current) return;
    import("leaflet").then((L) => {
      const map = mapInstanceRef.current as ReturnType<typeof L.map>;
      map.eachLayer((layer) => {
        if (layer instanceof L.Marker) map.removeLayer(layer);
      });
      markers.forEach((m) => {
        L.marker([m.lat, m.lng]).addTo(map).bindPopup(buildPopup(m));
      });
      if (markers.length > 0) {
        map.setView([markers[0].lat, markers[0].lng], 5);
      }
    });
  }, [markers]);

  return (
    <>
      <link
        rel="stylesheet"
        href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css"
        crossOrigin=""
      />
      <div ref={mapRef} style={{ height, borderRadius: 8, overflow: "hidden", zIndex: 0 }} />
    </>
  );
}
