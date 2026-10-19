-- Settled uploads keep their raw payload as gzip (see src/uploadArchive.ts); the plain column is emptied then.
ALTER TABLE `uploads` ADD `raw_payload_gz` blob;
