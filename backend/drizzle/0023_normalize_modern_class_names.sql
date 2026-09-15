-- The client's own skill dataset tags Gunner/Bard/Painter by their "modern" names (Gunslinger/
-- Songweaver/Muse), not the internal names this app uses everywhere else (ClassFilter dropdown,
-- ServerClassAvailability, icon files, and this same website's own classIcon() in app.js) - fixed
-- client-side (SkillDatabase.cs) and defensively server-side (uploadSchema.ts) going forward, but
-- rows already stored under the wrong name (found via a real report: "Ichika" on the Vasharti
-- fight, encounter_participants.id 1230) need a one-off fix too.
UPDATE `encounter_participants` SET `class_name` = 'Gunner' WHERE `class_name` = 'Gunslinger';
--> statement-breakpoint
UPDATE `encounter_participants` SET `class_name` = 'Bard' WHERE `class_name` = 'Songweaver';
--> statement-breakpoint
UPDATE `encounter_participants` SET `class_name` = 'Painter' WHERE `class_name` = 'Muse';
