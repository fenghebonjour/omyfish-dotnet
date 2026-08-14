import { FishUploader } from "@/components/FishUploader";

export default function HomePage() {
  return (
    <div className="flex flex-col gap-8">
      <section className="text-center">
        <h1 className="text-4xl font-bold text-blue-700 mb-2">
          Identify Any Fish
        </h1>
        <p className="text-gray-500 text-lg">
          Upload a photo and our AI will identify the species instantly.
        </p>
      </section>
      <FishUploader />
    </div>
  );
}
