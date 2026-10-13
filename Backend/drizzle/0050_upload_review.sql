-- Why an upload waits for review or was turned away, and when someone decided (see src/uploadReview.ts).
ALTER TABLE `uploads` ADD `review_reason` text;
--> statement-breakpoint
ALTER TABLE `uploads` ADD `reviewed_at` text;
