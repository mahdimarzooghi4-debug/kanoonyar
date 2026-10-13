-- Sprint 34: explicit, minimal cross-center collaboration proposals only.
-- Never an acceptance, commitment, shared resource allocation or actual execution.
BEGIN;
CREATE SCHEMA collaboration;
REVOKE ALL ON SCHEMA collaboration FROM PUBLIC;
CREATE TABLE collaboration.center_proposals (
 proposal_id uuid PRIMARY KEY,
 source_center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 target_center_id text NOT NULL REFERENCES centers.registry(center_id) ON DELETE RESTRICT,
 title text NOT NULL CHECK(length(btrim(title)) BETWEEN 1 AND 160),
 summary text NOT NULL CHECK(length(btrim(summary)) BETWEEN 1 AND 1200),
 created_by_subject text NOT NULL CHECK(length(btrim(created_by_subject))>0),
 recorded_at timestamptz NOT NULL DEFAULT transaction_timestamp(),
 CHECK(source_center_id<>target_center_id),
 CHECK(proposal_id<>'00000000-0000-0000-0000-000000000000'::uuid)
);
CREATE INDEX center_proposal_outgoing
 ON collaboration.center_proposals(source_center_id,proposal_id DESC);
CREATE INDEX center_proposal_incoming
 ON collaboration.center_proposals(target_center_id,proposal_id DESC);
CREATE TRIGGER center_proposal_immutable BEFORE UPDATE OR DELETE
 ON collaboration.center_proposals FOR EACH ROW EXECUTE FUNCTION programs.reject_draft_revision_mutation();
ALTER TABLE collaboration.center_proposals ENABLE ROW LEVEL SECURITY;
ALTER TABLE collaboration.center_proposals FORCE ROW LEVEL SECURITY;
CREATE POLICY center_proposal_explicit_party_scope ON collaboration.center_proposals
 USING (source_center_id=NULLIF(current_setting('kanoonyar.center_id',true),'')
     OR target_center_id=NULLIF(current_setting('kanoonyar.center_id',true),''))
 WITH CHECK (source_center_id=NULLIF(current_setting('kanoonyar.center_id',true),''));
REVOKE ALL ON collaboration.center_proposals FROM PUBLIC;
DO $roles$ BEGIN
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_collaboration_owner') THEN
  CREATE ROLE kanoonyar_collaboration_owner NOLOGIN NOINHERIT NOSUPERUSER
    NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION;
 END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_roles WHERE rolname='kanoonyar_collaboration_owner'
  AND NOT rolcanlogin AND NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb
  AND NOT rolcreaterole AND NOT rolreplication) THEN
   RAISE EXCEPTION 'Unsafe collaboration owner';
 END IF;
END;$roles$;
GRANT USAGE ON SCHEMA collaboration,programs,centers,governance
 TO kanoonyar_collaboration_owner;
GRANT SELECT ON centers.registry,centers.memberships TO kanoonyar_collaboration_owner;
GRANT SELECT,INSERT ON collaboration.center_proposals TO kanoonyar_collaboration_owner;
GRANT INSERT ON governance.command_audit,governance.outbox_events
 TO kanoonyar_collaboration_owner;
CREATE FUNCTION collaboration.record_center_proposal(
 p_id uuid,p_source text,p_target text,p_title text,p_summary text,
 p_actor text,p_audit uuid,p_outbox uuid,p_correlation text
) RETURNS uuid LANGUAGE plpgsql SECURITY DEFINER SET search_path=pg_catalog,pg_temp AS $fn$
BEGIN
 IF p_id IS NULL OR p_id='00000000-0000-0000-0000-000000000000'::uuid
  OR p_source IS NULL OR btrim(p_source)='' OR p_target IS NULL OR btrim(p_target)=''
  OR p_source=p_target OR p_title IS NULL OR length(btrim(p_title)) NOT BETWEEN 1 AND 160
  OR p_summary IS NULL OR length(btrim(p_summary)) NOT BETWEEN 1 AND 1200
  OR p_actor IS NULL OR btrim(p_actor)='' OR p_audit IS NULL OR p_outbox IS NULL
  OR p_correlation IS NULL OR btrim(p_correlation)='' THEN
  RAISE EXCEPTION 'Invalid explicit collaboration proposal' USING ERRCODE='22023';
 END IF;
 IF p_source IS DISTINCT FROM NULLIF(current_setting('kanoonyar.center_id',true),'')
  OR NOT EXISTS(SELECT 1 FROM centers.memberships m JOIN centers.registry c
   ON c.center_id=m.center_id WHERE m.center_id=p_source
    AND m.subject_id=p_actor AND m.is_active AND c.is_active) THEN
  RAISE EXCEPTION 'Source membership or center scope absent' USING ERRCODE='42501';
 END IF;
 IF NOT EXISTS(SELECT 1 FROM centers.registry
  WHERE center_id=p_target AND is_active) THEN
  RAISE EXCEPTION 'Target center unavailable' USING ERRCODE='23503';
 END IF;
 -- UUID identity is unique; duplicate attempt fails atomically and never emits a second audit/outbox.
 INSERT INTO collaboration.center_proposals
 (proposal_id,source_center_id,target_center_id,title,summary,created_by_subject)
 VALUES(p_id,p_source,p_target,btrim(p_title),btrim(p_summary),p_actor);
 INSERT INTO governance.command_audit(audit_id,occurred_at,actor_subject_id,
  center_id,action_code,target_type,target_id,correlation_id,metadata)
 VALUES(p_audit,transaction_timestamp(),p_actor,p_source,
  'CENTER_COLLABORATION_PROPOSAL_RECORDED','center_collaboration_proposal',
  p_id::text,p_correlation,jsonb_build_object('target_center_id',p_target,
  'visibility','EXPLICIT_TARGET_ONLY','is_accepted',false));
 INSERT INTO governance.outbox_events(event_id,audit_id,event_type,aggregate_type,
  aggregate_id,occurred_at,payload)
 VALUES(p_outbox,p_audit,'CenterCollaborationProposalRecorded',
  'center_collaboration_proposal',p_id::text,transaction_timestamp(),
  jsonb_build_object('proposal_id',p_id,'source_center_id',p_source,
  'target_center_id',p_target,'visibility','EXPLICIT_TARGET_ONLY',
  'is_accepted',false));
 RETURN p_id;
END;$fn$;
ALTER FUNCTION collaboration.record_center_proposal(
 uuid,text,text,text,text,text,uuid,uuid,text) OWNER TO kanoonyar_collaboration_owner;
REVOKE ALL ON FUNCTION collaboration.record_center_proposal(
 uuid,text,text,text,text,text,uuid,uuid,text) FROM PUBLIC;
COMMENT ON TABLE collaboration.center_proposals IS
 'Immutable minimal explicitly directed proposal, never proof of acceptance or permission to read origin center private data';
COMMIT;
