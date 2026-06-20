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

export function ObservationMap({ markers, height = "300px" }: ObservationMapProps) {
  const mapRef = useRef<HTMLDivElement>(null);
  const mapInstanceRef = useRef<unknown>(null);

  useEffect(() => {
    if (!mapRef.current || mapInstanceRef.current) return;

    // Dynamically import Leaflet to avoid SSR issues
    import("leaflet").then((L) => {
      // Leaflet default icon fix for bundlers
      // @ts-expect-error _getIconUrl is internal
      delete L.Icon.Default.prototype._getIconUrl;
      L.Icon.Default.mergeOptions({
        iconRetinaUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon-2x.png",
        iconUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-icon.png",
        shadowUrl: "https://unpkg.com/leaflet@1.9.4/dist/images/marker-shadow.png",
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
        L.marker([m.lat, m.lng]).addTo(map).bindPopup(m.label);
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
        L.marker([m.lat, m.lng]).addTo(map).bindPopup(m.label);
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
