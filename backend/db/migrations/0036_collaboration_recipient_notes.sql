-- Sprint 36: recipient-private notes for real S34 collaboration proposals.
-- Notes are observations for internal triage only: NOT approval, denial or acceptance.
BEGIN;
ALTER TABLE collaboration.center_proposals
 ADD CONSTRAINT collaboration_proposal_target_unique UNIQUE(proposal_id,target_center_id);
CREATE TABLE collaboration.recipient_private_notes (
 note_id uuid PRIMARY KEY,
 proposal_id uuid NOT NULL,
 target_center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 note_text text NOT NULL CHECK(length(btrim(note_text)) BETWEEN 1 AND 1200),
 recorded_by_subject text NOT NULL CHECK(length(btrim(recorded_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 FOREIGN KEY(proposal_id,target_center_id)
  REFERENCES collaboration.center_proposals(proposal_id,target_center_id) ON DELETE RESTRICT,
 CHECK(note_id<>'00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE INDEX recipient_private_notes_keyset ON collaboration.recipient_private_notes
 (target_center_id,proposal_id,note_id DESC);
CREATE TRIGGER recipient_notes_immutable BEFORE UPDATE OR DELETE
 ON collaboration.recipient_private_notes FOR EACH ROW
 EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE collaboration.recipient_private_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE collaboration.recipient_private_notes FORCE ROW LEVEL SECURITY;
CREATE POLICY recipient_notes_target_only ON collaboration.recipient_private_notes
 USING(target_center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK(target_center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON collaboration.recipient_private_notes FROM PUBLIC;
DO $role$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_recipient_note_owner') THEN
  CREATE ROLE kanoonyar_recipient_note_owner NOLOGIN NOINHERIT NOSUPERUSER
   NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_recipient_note_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls
  AND NOT rolcreatedb AND NOT rolcreaterole AND NOT rolreplication) THEN
  RAISE EXCEPTION 'Unsafe private recipient note owner';
 END IF;
END;$role$;
GRANT USAGE ON SCHEMA collaboration,centers,governance TO kanoonyar_recipient_note_owner;
GRANT SELECT ON collaboration.center_proposals,centers.registry,centers.memberships
 TO kanoonyar_recipient_note_owner;
GRANT INSERT ON collaboration.recipient_private_notes TO kanoonyar_recipient_note_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events TO kanoonyar_recipient_note_owner;
CREATE FUNCTION collaboration.append_recipient_private_note(
 p_note uuid,p_proposal uuid,p_target text,p_text text,p_actor text,
 p_audit uuid,p_outbox uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_note IS NULL OR p_note='00000000-0000-0000-0000-000000000000'::uuid
  OR p_proposal IS NULL OR p_proposal='00000000-0000-0000-0000-000000000000'::uuid
  OR p_target IS NULL OR btrim(p_target)='' OR p_text IS NULL
  OR length(btrim(p_text)) NOT BETWEEN 1 AND 1200
  OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_outbox IS NULL
  OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid recipient-private note' USING ERRCODE='22023';
 END IF;
 IF p_target IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
   ON c.center_id=m.center_id WHERE m.center_id=p_target AND m.subject_id=p_actor
    AND m.is_active AND c.is_active) THEN
  RAISE EXCEPTION 'Recipient center membership or scope absent' USING ERRCODE='42501';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM collaboration.center_proposals p
   WHERE p.proposal_id=p_proposal AND p.target_center_id=p_target) THEN
  RAISE EXCEPTION 'Recipient proposal scope absent' USING ERRCODE='42501';
 END IF;
 INSERT INTO collaboration.recipient_private_notes
  (note_id,proposal_id,target_center_id,note_text,recorded_by_subject)
 VALUES(p_note,p_proposal,p_target,btrim(p_text),p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,
   center_id,action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_target,
  'RECIPIENT_PRIVATE_NOTE_RECORDED','collaboration_recipient_note',p_note::text,
  p_correlation,jsonb_build_object('proposal_id',p_proposal,'visibility','RECIPIENT_INTERNAL',
   'is_accepted',false,'is_executed',false));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
   aggregate_id,occurred_at,payload)
 VALUES(p_outbox,p_audit,'RecipientPrivateNoteRecorded','collaboration_recipient_note',
  p_note::text,transaction_timestamp(),
  jsonb_build_object('target_center_id',p_target,'proposal_id',p_proposal,'note_id',p_note,
   'visibility','RECIPIENT_INTERNAL','is_accepted',false,'is_executed',false));
 RETURN p_note;
END;$fn$;
ALTER FUNCTION collaboration.append_recipient_private_note(
 uuid,uuid,text,text,text,uuid,uuid,text) OWNER TO kanoonyar_recipient_note_owner;
REVOKE ALL ON FUNCTION collaboration.append_recipient_private_note(
 uuid,uuid,text,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE collaboration.recipient_private_notes IS
 'Internal recipient-only private review notes. Does not imply approval, denial, acceptance, or execution. Not visible to sender.';
COMMIT;
