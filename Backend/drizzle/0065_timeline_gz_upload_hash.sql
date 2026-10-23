-- encounter_timelines: the time axis is stored as gzip (data_gz) and data_json is emptied; rows are packed by the archive job (uploadArchive.ts).
ALTER TABLE `encounter_timelines` ADD `data_gz` blob;
--> statement-breakpoint
-- uploads: hash of the payload of a merged upload, so the very same payload from the same uploader is not filed a second time.
ALTER TABLE `uploads` ADD `payload_hash` text;
--> statement-breakpoint
CREATE INDEX `uploads_payload_hash_idx` ON `uploads` (`payload_hash`);
