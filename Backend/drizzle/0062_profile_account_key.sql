-- A hash of the account key of the login's character list (own character only), the same on every character of one account.
ALTER TABLE `player_profiles` ADD `account_key` text;
