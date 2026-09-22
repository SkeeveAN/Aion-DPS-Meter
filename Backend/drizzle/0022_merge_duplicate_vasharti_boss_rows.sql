-- Real incident: migration 0016 deliberately created a SECOND "Brigade General Vasharti" boss row
-- for Lost Rentus Base, reusing the exact same name already used for Rentus-Basis's own row (see
-- 0016's own comment: "same name, different bosses.id pattern"). That assumption didn't hold up -
-- resolveBossId (matching/merge.ts) matches purely by name with no instance/zone context at all
-- (the client uploads no such thing, see backend/README.md's own "keine Instanz→Boss-Datenbank"
-- remarks), so two rows sharing one exact name are NOT actually distinguishable server-side - the
-- first real upload for either (a Lost Rentus Base HM run, confirmed by the user) silently landed
-- on the OTHER row (Rentus-Basis's, whichever `bosses` happened to return first). Confirmed via the
-- data that Rentus-Basis's own row had zero real encounters before this - its one and only
-- encounter (id determined below) IS this same mis-filed run.
--
-- Fix: merge into a single row under Lost Rentus Base (the instance actually confirmed played),
-- eliminating the ambiguity entirely rather than trying to out-guess `resolveBossId` - matches this
-- project's own "don't pretend a distinction the data can't support" rule (see Tiamat's Hidden
-- Space/Unterschlupf photo-sharing, Lost Rentus Base/Lost Refuge image reuse). Rentus-Basis's own
-- instance row is left in place (unmapped from any server already, per migration 0018/0019) rather
-- than deleted - same "never just discard a row" pattern as everywhere else in this schema.
UPDATE `encounters` SET `boss_id` = (
  SELECT id FROM `bosses`
  WHERE `name` = 'Brigade General Vasharti'
    AND `instance_id` = (SELECT id FROM `instances` WHERE `name` = 'Lost Rentus Base')
)
WHERE `boss_id` = (
  SELECT id FROM `bosses`
  WHERE `name` = 'Brigade General Vasharti'
    AND `instance_id` = (SELECT id FROM `instances` WHERE `name` = 'Rentus-Basis')
);
--> statement-breakpoint

DELETE FROM `bosses`
WHERE `name` = 'Brigade General Vasharti'
  AND `instance_id` = (SELECT id FROM `instances` WHERE `name` = 'Rentus-Basis');
