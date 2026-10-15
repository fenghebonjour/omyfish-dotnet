-- Set once an admin uploads a real photo via MinIOImageStorageService — null for the seeded
-- placeholder products, which keep their inline data: URI directly in image_url.
ALTER TABLE shop.products ADD COLUMN image_storage_key VARCHAR(512);
