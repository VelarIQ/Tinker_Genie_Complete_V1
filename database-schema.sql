--
-- PostgreSQL database dump
--

-- Dumped from database version 16.9 (Ubuntu 16.9-1.pgdg24.04+1)
-- Dumped by pg_dump version 16.9 (Ubuntu 16.9-0ubuntu0.24.04.1)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: pg_stat_statements; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pg_stat_statements WITH SCHEMA public;


--
-- Name: EXTENSION pg_stat_statements; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION pg_stat_statements IS 'track planning and execution statistics of all SQL statements executed';


--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION pgcrypto IS 'cryptographic functions';


--
-- Name: uuid-ossp; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS "uuid-ossp" WITH SCHEMA public;


--
-- Name: EXTENSION "uuid-ossp"; Type: COMMENT; Schema: -; Owner: 
--

COMMENT ON EXTENSION "uuid-ossp" IS 'generate universally unique identifiers (UUIDs)';


--
-- Name: decrypt_pii(text); Type: FUNCTION; Schema: public; Owner: genie_admin
--

CREATE FUNCTION public.decrypt_pii(encrypted_data text) RETURNS text
    LANGUAGE plpgsql
    AS $$
BEGIN
    RETURN convert_from(decrypt(decode(encrypted_data, 'base64'), 'TinkerGenie2025!Key', 'aes'), 'UTF8');
END;
$$;


ALTER FUNCTION public.decrypt_pii(encrypted_data text) OWNER TO genie_admin;

--
-- Name: encrypt_pii(text); Type: FUNCTION; Schema: public; Owner: genie_admin
--

CREATE FUNCTION public.encrypt_pii(data text) RETURNS text
    LANGUAGE plpgsql
    AS $$
BEGIN
    RETURN encode(encrypt(data::bytea, 'TinkerGenie2025!Key', 'aes'), 'base64');
END;
$$;


ALTER FUNCTION public.encrypt_pii(data text) OWNER TO genie_admin;

--
-- Name: encrypt_pii_jsonb(jsonb); Type: FUNCTION; Schema: public; Owner: genie_admin
--

CREATE FUNCTION public.encrypt_pii_jsonb(data jsonb) RETURNS text
    LANGUAGE plpgsql
    AS $$
BEGIN
    RETURN encode(encrypt(data::text::bytea, 'TinkerGenie2025!Key', 'aes'), 'base64');
END;
$$;


ALTER FUNCTION public.encrypt_pii_jsonb(data jsonb) OWNER TO genie_admin;

--
-- Name: get_personalized_prompt(character varying); Type: FUNCTION; Schema: public; Owner: genie_admin
--

CREATE FUNCTION public.get_personalized_prompt(p_user_id character varying) RETURNS TABLE(day_number integer, prompt_title character varying, prompt_text text, fill_in_blanks text[], follow_up_questions text[], user_first_name character varying, completed boolean)
    LANGUAGE plpgsql
    AS $$
BEGIN
    RETURN QUERY
    SELECT 
        ldp.day_number,
        ldp.prompt_title,
        ldp.prompt_text,
        ldp.fill_in_blanks,
        ldp.follow_up_questions,
        ud.first_name,
        EXISTS (
            SELECT 1 FROM user_prompt_progress upp 
            WHERE upp.user_id = p_user_id 
            AND upp.day_number = ud.current_day 
            AND upp.completed_at IS NOT NULL
        ) as completed
    FROM user_data ud
    JOIN leadership_daily_prompts ldp ON ldp.day_number = ud.current_day
    WHERE ud.user_id = p_user_id
    AND ldp.is_active = true
    ORDER BY ldp.version DESC
    LIMIT 1;
END;
$$;


ALTER FUNCTION public.get_personalized_prompt(p_user_id character varying) OWNER TO genie_admin;

--
-- Name: get_users_for_nudge(); Type: FUNCTION; Schema: public; Owner: genie_admin
--

CREATE FUNCTION public.get_users_for_nudge() RETURNS TABLE(user_id uuid, email character varying, first_name character varying, hours_inactive numeric, preferred_time time without time zone, timezone character varying)
    LANGUAGE plpgsql
    AS $$
BEGIN
    RETURN QUERY
    WITH last_nudge AS (
        SELECT 
            un.user_id,
            MAX(un.sent_at) as last_nudge_sent
        FROM user_nudges un
        GROUP BY un.user_id
    ),
    user_status AS (
        SELECT 
            u.id,
            u.email,
            u.first_name,
            MAX(c.created_at) as last_activity,
            EXTRACT(EPOCH FROM (NOW() - MAX(c.created_at)))/3600 as hours_inactive,
            up.nudge_enabled,
            up.nudge_hours_inactive,
            up.nudge_frequency_hours,
            up.preferred_nudge_time,
            up.timezone,
            ln.last_nudge_sent
        FROM users u
        LEFT JOIN conversations c ON u.id = c.user_id
        LEFT JOIN user_preferences up ON u.id = up.user_id
        LEFT JOIN last_nudge ln ON u.id = ln.user_id
        WHERE u.is_active = true
        GROUP BY u.id, u.email, u.first_name, up.nudge_enabled, 
                 up.nudge_hours_inactive, up.nudge_frequency_hours,
                 up.preferred_nudge_time, up.timezone, ln.last_nudge_sent
    )
    SELECT 
        us.id,
        us.email,
        us.first_name,
        us.hours_inactive::DECIMAL,
        us.preferred_nudge_time,
        us.timezone
    FROM user_status us
    WHERE us.nudge_enabled = true
        AND us.hours_inactive >= us.nudge_hours_inactive
        AND (us.last_nudge_sent IS NULL 
             OR us.last_nudge_sent < NOW() - MAKE_INTERVAL(hours => us.nudge_frequency_hours));
END;
$$;


ALTER FUNCTION public.get_users_for_nudge() OWNER TO genie_admin;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: admin_notifications; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.admin_notifications (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    notification_type character varying(50) NOT NULL,
    title character varying(200) NOT NULL,
    message text NOT NULL,
    metadata jsonb,
    is_read boolean DEFAULT false,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamp without time zone
);


ALTER TABLE public.admin_notifications OWNER TO genie_admin;

--
-- Name: admin_users; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.admin_users (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    email character varying(255) NOT NULL,
    role character varying(50) DEFAULT 'viewer'::character varying,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.admin_users OWNER TO genie_admin;

--
-- Name: archived_users; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.archived_users (
    id uuid,
    user_id character varying(100),
    name character varying(100),
    business_name character varying(200),
    email character varying(255),
    current_day integer,
    joined_date timestamp without time zone,
    preferences jsonb,
    metadata jsonb,
    last_active timestamp without time zone,
    created_at timestamp without time zone,
    updated_at timestamp without time zone,
    signup_date timestamp without time zone,
    last_prompt_sent timestamp without time zone,
    app_opens jsonb,
    password_hash character varying(255),
    last_login timestamp without time zone,
    is_active boolean,
    first_name character varying(100),
    external_id character varying(50),
    entity_type character varying(20),
    can_access_tinker boolean,
    username character varying(255),
    full_name character varying(255),
    associated_business character varying(255),
    business_role character varying(100),
    gdpr_consent_date timestamp without time zone,
    data_retention_period character varying(50),
    encryption_status character varying(20),
    jurisdiction character varying(10),
    account_expiry timestamp without time zone,
    last_sync timestamp without time zone,
    encrypted_at timestamp without time zone,
    email_encrypted text,
    full_name_encrypted text,
    business_profile_encrypted text,
    entityid integer,
    mentor character varying(255),
    productcategory character varying(255),
    product character varying(255),
    phase character varying(255),
    stage character varying(255),
    business_profile jsonb,
    username_encrypted text
);


ALTER TABLE public.archived_users OWNER TO genie_admin;

--
-- Name: audit_log; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.audit_log (
    id integer NOT NULL,
    action character varying(50),
    entity_type character varying(50),
    entity_id integer,
    "timestamp" timestamp without time zone,
    compliance_type character varying(20)
);


ALTER TABLE public.audit_log OWNER TO genie_admin;

--
-- Name: audit_log_id_seq; Type: SEQUENCE; Schema: public; Owner: genie_admin
--

CREATE SEQUENCE public.audit_log_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.audit_log_id_seq OWNER TO genie_admin;

--
-- Name: audit_log_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: genie_admin
--

ALTER SEQUENCE public.audit_log_id_seq OWNED BY public.audit_log.id;


--
-- Name: baseline_content; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.baseline_content (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    content text NOT NULL,
    metadata jsonb,
    insights jsonb,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.baseline_content OWNER TO genie_admin;

--
-- Name: content_ingestion_log; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.content_ingestion_log (
    file_id character varying(255) NOT NULL,
    file_name character varying(500),
    category character varying(50),
    source_url text,
    content_type character varying(50),
    chunks_created integer DEFAULT 0,
    vector_ids text[],
    ingested_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.content_ingestion_log OWNER TO genie_admin;

--
-- Name: conversation_messages; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.conversation_messages (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    conversation_id uuid,
    sender character varying(20) NOT NULL,
    message_text text NOT NULL,
    message_type character varying(50) DEFAULT 'text'::character varying,
    ai_model_used character varying(50),
    generation_context jsonb,
    user_reaction character varying(50),
    processing_time_ms integer,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    user_id uuid,
    content text,
    is_user boolean DEFAULT false
);


ALTER TABLE public.conversation_messages OWNER TO genie_admin;

--
-- Name: conversations; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.conversations (
    id integer NOT NULL,
    conversation_id character varying(36) NOT NULL,
    user_id character varying(100) NOT NULL,
    user_message text NOT NULL,
    ai_response text NOT NULL,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.conversations OWNER TO genie_admin;

--
-- Name: conversations_id_seq; Type: SEQUENCE; Schema: public; Owner: genie_admin
--

CREATE SEQUENCE public.conversations_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.conversations_id_seq OWNER TO genie_admin;

--
-- Name: conversations_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: genie_admin
--

ALTER SEQUENCE public.conversations_id_seq OWNED BY public.conversations.id;


--
-- Name: curriculum_modules; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.curriculum_modules (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    tenant_id uuid,
    name character varying(200) NOT NULL,
    description text,
    module_type character varying(50) NOT NULL,
    order_sequence integer NOT NULL,
    duration_weeks integer DEFAULT 1,
    prerequisites uuid[],
    learning_objectives text[],
    content_summary text,
    version character varying(20) DEFAULT '1.0.0'::character varying,
    is_active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.curriculum_modules OWNER TO genie_admin;

--
-- Name: curriculum_prompts; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.curriculum_prompts (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    module_id uuid,
    day_number integer NOT NULL,
    prompt_name character varying(200) NOT NULL,
    base_prompt text NOT NULL,
    prompt_type character varying(50) NOT NULL,
    estimated_time_minutes integer DEFAULT 10,
    difficulty_level integer DEFAULT 1,
    tags text[],
    success_criteria text[],
    is_dynamic boolean DEFAULT true,
    version character varying(20) DEFAULT '1.0.0'::character varying,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.curriculum_prompts OWNER TO genie_admin;

--
-- Name: genie_conversations; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.genie_conversations (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    genie_instance_id uuid,
    user_id uuid,
    title character varying(200),
    conversation_type character varying(50) DEFAULT 'daily_prompt'::character varying,
    context_tags text[],
    status character varying(50) DEFAULT 'active'::character varying,
    message_count integer DEFAULT 0,
    satisfaction_rating integer,
    started_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    last_message_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    ended_at timestamp without time zone,
    key_insights jsonb,
    action_items jsonb,
    emotional_context jsonb,
    total_messages integer DEFAULT 0,
    context_summary text,
    is_active boolean DEFAULT true,
    leadership_day integer,
    conversation_context character varying(50) DEFAULT 'general'::character varying
);


ALTER TABLE public.genie_conversations OWNER TO genie_admin;

--
-- Name: daily_activity; Type: VIEW; Schema: public; Owner: postgres
--

CREATE VIEW public.daily_activity AS
 SELECT date(cm.created_at) AS date,
    count(DISTINCT gc.user_id) AS active_users,
    count(*) AS total_messages,
    avg(
        CASE
            WHEN ((cm.sender)::text = 'user'::text) THEN 1
            ELSE 0
        END) AS user_message_ratio
   FROM (public.conversation_messages cm
     JOIN public.genie_conversations gc ON ((cm.conversation_id = gc.id)))
  GROUP BY (date(cm.created_at));


ALTER VIEW public.daily_activity OWNER TO postgres;

--
-- Name: genie_instances; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.genie_instances (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    tenant_id uuid,
    name character varying(200) NOT NULL,
    type character varying(50) NOT NULL,
    hierarchy_level integer NOT NULL,
    parent_genie_id uuid,
    owner_user_id uuid,
    persona_config jsonb DEFAULT '{}'::jsonb NOT NULL,
    curriculum_assignments uuid[],
    permissions jsonb,
    is_active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    user_id uuid
);


ALTER TABLE public.genie_instances OWNER TO genie_admin;

--
-- Name: knowledge_base; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.knowledge_base (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    title character varying(200) NOT NULL,
    content text NOT NULL,
    category character varying(100),
    tags text[],
    source_url character varying(500),
    relevance_score real DEFAULT 1.0,
    is_active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.knowledge_base OWNER TO genie_admin;

--
-- Name: knowledge_base_content; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.knowledge_base_content (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    source_folder character varying(100) NOT NULL,
    document_id character varying(255) NOT NULL,
    document_name character varying(500) NOT NULL,
    content text NOT NULL,
    content_type character varying(50) DEFAULT 'text'::character varying,
    metadata jsonb,
    content_hash character varying(64),
    indexed_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    version integer DEFAULT 1,
    is_active boolean DEFAULT true
);


ALTER TABLE public.knowledge_base_content OWNER TO genie_admin;

--
-- Name: leadership_content; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.leadership_content (
    content_id character varying(255) NOT NULL,
    type character varying(50),
    day_number integer,
    title character varying(500),
    content text,
    prompt_phase character varying(50),
    fill_in_blanks jsonb,
    location_info jsonb,
    reference_note text,
    category character varying(100),
    source_file character varying(500),
    source_url text,
    file_id character varying(255),
    chunk_index integer,
    total_chunks integer,
    modified_time timestamp without time zone,
    created_at timestamp without time zone DEFAULT now(),
    updated_at timestamp without time zone DEFAULT now(),
    prompt_text text
);


ALTER TABLE public.leadership_content OWNER TO genie_admin;

--
-- Name: leadership_daily_prompts; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.leadership_daily_prompts (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    day_number integer NOT NULL,
    prompt_title character varying(200) NOT NULL,
    prompt_text text NOT NULL,
    fill_in_blanks text[],
    task_instructions text,
    estimated_time_minutes integer DEFAULT 10,
    version integer DEFAULT 1,
    document_id character varying(255),
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_by character varying(100) DEFAULT 'system'::character varying,
    is_active boolean DEFAULT true,
    follow_up_questions jsonb DEFAULT '[]'::jsonb,
    fill_in_blanks_jsonb jsonb,
    CONSTRAINT leadership_daily_prompts_day_number_check CHECK (((day_number >= 1) AND (day_number <= 180)))
);


ALTER TABLE public.leadership_daily_prompts OWNER TO genie_admin;

--
-- Name: notification_log; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.notification_log (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id character varying(100) NOT NULL,
    notification_type character varying(50),
    day_number integer,
    message text,
    delivered boolean DEFAULT false,
    clicked boolean DEFAULT false,
    sent_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.notification_log OWNER TO genie_admin;

--
-- Name: nudge_tracking; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.nudge_tracking (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid,
    nudge_type character varying(50) NOT NULL,
    sent_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    response_received boolean DEFAULT false,
    response_at timestamp without time zone,
    effectiveness_score integer,
    metadata jsonb
);


ALTER TABLE public.nudge_tracking OWNER TO genie_admin;

--
-- Name: preference_change_log; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.preference_change_log (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id character varying(255),
    action character varying(100),
    "timestamp" timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    details jsonb
);


ALTER TABLE public.preference_change_log OWNER TO postgres;

--
-- Name: user_responses; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_responses (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id character varying(255),
    prompt_id uuid,
    conversation_id uuid,
    response_text text,
    response_type character varying(50) DEFAULT 'text'::character varying,
    sentiment_score numeric(3,2),
    engagement_score integer,
    insights jsonb,
    follow_up_needed boolean DEFAULT false,
    completed_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    time_spent_minutes integer
);


ALTER TABLE public.user_responses OWNER TO genie_admin;

--
-- Name: prompt_completion_stats; Type: VIEW; Schema: public; Owner: postgres
--

CREATE VIEW public.prompt_completion_stats AS
 SELECT date(completed_at) AS date,
    count(DISTINCT user_id) AS users_completed,
    avg(time_spent_minutes) AS avg_time_spent,
    count(*) AS total_completions
   FROM public.user_responses
  GROUP BY (date(completed_at));


ALTER VIEW public.prompt_completion_stats OWNER TO postgres;

--
-- Name: prompt_tracking; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.prompt_tracking (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id character varying(255),
    prompt_id uuid,
    sent_at timestamp without time zone,
    opened_at timestamp without time zone,
    completed_at timestamp without time zone,
    nudge_sent_at timestamp without time zone,
    response_quality integer,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.prompt_tracking OWNER TO genie_admin;

--
-- Name: push_subscriptions; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.push_subscriptions (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id character varying(255),
    platform character varying(20),
    endpoint text,
    keys jsonb,
    device_token text,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    is_active boolean DEFAULT true
);


ALTER TABLE public.push_subscriptions OWNER TO genie_admin;

--
-- Name: tenants; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.tenants (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    name character varying(200) NOT NULL,
    domain character varying(100),
    subscription_tier character varying(50) DEFAULT 'standard'::character varying,
    is_active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.tenants OWNER TO genie_admin;

--
-- Name: usage_metrics; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.usage_metrics (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid,
    date date NOT NULL,
    login_count integer DEFAULT 0,
    messages_sent integer DEFAULT 0,
    prompts_completed integer DEFAULT 0,
    total_time_minutes integer DEFAULT 0,
    platform character varying(50)
);


ALTER TABLE public.usage_metrics OWNER TO genie_admin;

--
-- Name: user_activity_log; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_activity_log (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    activity_type character varying(50) NOT NULL,
    activity_data jsonb,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.user_activity_log OWNER TO genie_admin;

--
-- Name: user_analytics; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_analytics (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id character varying(255),
    date date NOT NULL,
    daily_engagement_minutes integer DEFAULT 0,
    prompts_completed integer DEFAULT 0,
    conversations_initiated integer DEFAULT 0,
    mood_score numeric(3,2),
    progress_velocity numeric(5,2),
    engagement_streak integer DEFAULT 0,
    preference_updates integer DEFAULT 0,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.user_analytics OWNER TO genie_admin;

--
-- Name: user_conversations_partition; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_partition (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
)
PARTITION BY HASH (user_id);


ALTER TABLE public.user_conversations_partition OWNER TO genie_admin;

--
-- Name: user_conversations_p0; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p0 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p0 OWNER TO genie_admin;

--
-- Name: user_conversations_p1; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p1 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p1 OWNER TO genie_admin;

--
-- Name: user_conversations_p2; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p2 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p2 OWNER TO genie_admin;

--
-- Name: user_conversations_p3; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p3 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p3 OWNER TO genie_admin;

--
-- Name: user_conversations_p4; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p4 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p4 OWNER TO genie_admin;

--
-- Name: user_conversations_p5; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p5 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p5 OWNER TO genie_admin;

--
-- Name: user_conversations_p6; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p6 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p6 OWNER TO genie_admin;

--
-- Name: user_conversations_p7; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p7 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p7 OWNER TO genie_admin;

--
-- Name: user_conversations_p8; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p8 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p8 OWNER TO genie_admin;

--
-- Name: user_conversations_p9; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_conversations_p9 (
    user_id character varying(255) NOT NULL,
    conversation_id uuid NOT NULL,
    messages jsonb,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_conversations_p9 OWNER TO genie_admin;

--
-- Name: user_data; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_data (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id character varying(100) NOT NULL,
    name character varying(100),
    business_name character varying(200),
    email character varying(255),
    current_day integer DEFAULT 1,
    joined_date timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    preferences jsonb,
    metadata jsonb,
    last_active timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    signup_date timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    last_prompt_sent timestamp without time zone,
    app_opens jsonb DEFAULT '[]'::jsonb,
    password_hash character varying(255),
    last_login timestamp without time zone,
    is_active boolean DEFAULT true,
    first_name character varying(100),
    external_id character varying(50),
    entity_type character varying(20),
    can_access_tinker boolean,
    username character varying(255),
    full_name character varying(255),
    associated_business character varying(255),
    business_role character varying(100),
    gdpr_consent_date timestamp without time zone,
    data_retention_period character varying(50),
    encryption_status character varying(20),
    jurisdiction character varying(10),
    account_expiry timestamp without time zone,
    last_sync timestamp without time zone,
    encrypted_at timestamp without time zone,
    email_encrypted text,
    full_name_encrypted text,
    business_profile_encrypted text,
    entityid integer,
    mentor character varying(255),
    productcategory character varying(255),
    product character varying(255),
    phase character varying(255),
    stage character varying(255),
    business_profile jsonb,
    username_encrypted text
);


ALTER TABLE public.user_data OWNER TO genie_admin;

--
-- Name: users; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.users (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    tenant_id uuid,
    tbb_user_id integer NOT NULL,
    email character varying(255) NOT NULL,
    first_name character varying(100),
    last_name character varying(100),
    role character varying(50) DEFAULT 'tinker_member'::character varying,
    is_tinker_member boolean DEFAULT true,
    google_id character varying(255),
    last_login timestamp without time zone,
    is_active boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    username character varying(255)
);


ALTER TABLE public.users OWNER TO genie_admin;

--
-- Name: user_engagement_stats; Type: VIEW; Schema: public; Owner: postgres
--

CREATE VIEW public.user_engagement_stats AS
 SELECT u.email,
    u.first_name,
    u.last_name,
    count(DISTINCT gc.id) AS total_conversations,
    count(cm.id) AS total_messages,
    avg(gc.satisfaction_rating) AS avg_satisfaction,
    max(gc.last_message_at) AS last_active,
    count(DISTINCT date(gc.started_at)) AS active_days
   FROM ((public.users u
     LEFT JOIN public.genie_conversations gc ON ((u.id = gc.user_id)))
     LEFT JOIN public.conversation_messages cm ON ((gc.id = cm.conversation_id)))
  GROUP BY u.id, u.email, u.first_name, u.last_name;


ALTER VIEW public.user_engagement_stats OWNER TO postgres;

--
-- Name: user_notifications; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_notifications (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id character varying(100) NOT NULL,
    push_subscription jsonb,
    notification_time time without time zone DEFAULT '09:00:00'::time without time zone,
    timezone character varying(50) DEFAULT 'America/New_York'::character varying,
    enabled boolean DEFAULT true,
    last_sent_day integer,
    last_sent_at timestamp without time zone,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP
);


ALTER TABLE public.user_notifications OWNER TO genie_admin;

--
-- Name: user_nudges; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_nudges (
    id integer NOT NULL,
    user_id uuid,
    nudge_type character varying(50) DEFAULT 'inactivity'::character varying,
    hours_inactive numeric(4,1),
    sent_at timestamp without time zone DEFAULT now(),
    responded_at timestamp without time zone,
    response_time_minutes integer,
    channel character varying(20) DEFAULT 'email'::character varying,
    created_at timestamp without time zone DEFAULT now()
);


ALTER TABLE public.user_nudges OWNER TO genie_admin;

--
-- Name: user_nudges_id_seq; Type: SEQUENCE; Schema: public; Owner: genie_admin
--

CREATE SEQUENCE public.user_nudges_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.user_nudges_id_seq OWNER TO genie_admin;

--
-- Name: user_nudges_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: genie_admin
--

ALTER SEQUENCE public.user_nudges_id_seq OWNED BY public.user_nudges.id;


--
-- Name: user_preferences; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_preferences (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid,
    communication_style character varying(20) DEFAULT 'medium'::character varying,
    prompt_delivery_time time without time zone DEFAULT '09:00:00'::time without time zone,
    timezone character varying(50) DEFAULT 'America/New_York'::character varying,
    journey_started_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    current_day integer DEFAULT 1,
    journey_paused boolean DEFAULT false,
    pause_reason text,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT user_preferences_communication_style_check CHECK (((communication_style)::text = ANY ((ARRAY['short'::character varying, 'medium'::character varying, 'long'::character varying])::text[])))
);


ALTER TABLE public.user_preferences OWNER TO genie_admin;

--
-- Name: user_profiles; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_profiles (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id character varying(255),
    communication_style character varying(50) DEFAULT 'balanced'::character varying,
    preferred_response_length character varying(20) DEFAULT 'medium'::character varying,
    timezone character varying(50) DEFAULT 'America/New_York'::character varying,
    business_type character varying(100),
    business_stage character varying(50),
    current_challenges text[],
    goals text[],
    onboarding_completed boolean DEFAULT false,
    personality_insights jsonb,
    preferred_prompt_time time without time zone DEFAULT '09:00:00'::time without time zone,
    nudge_enabled boolean DEFAULT true,
    email_summary_enabled boolean DEFAULT true,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    journey_paused boolean DEFAULT false,
    journey_paused_at timestamp without time zone,
    journey_resumed_at timestamp without time zone,
    onboarding_completed_at timestamp without time zone,
    first_name character varying(100),
    business_name character varying(255)
);


ALTER TABLE public.user_profiles OWNER TO genie_admin;

--
-- Name: user_prompt_deliveries; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_prompt_deliveries (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id uuid NOT NULL,
    day_number integer NOT NULL,
    delivered_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    completed_at timestamp without time zone
);


ALTER TABLE public.user_prompt_deliveries OWNER TO genie_admin;

--
-- Name: user_prompt_progress; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_prompt_progress (
    id uuid DEFAULT public.uuid_generate_v4() NOT NULL,
    user_id uuid,
    day_number integer NOT NULL,
    prompt_version integer DEFAULT 1,
    status character varying(20) DEFAULT 'pending'::character varying,
    delivered_at timestamp without time zone,
    started_at timestamp without time zone,
    completed_at timestamp without time zone,
    user_responses jsonb,
    follow_up_questions jsonb,
    follow_up_responses jsonb,
    reflection_notes text,
    missed_count integer DEFAULT 0,
    last_nudge_sent timestamp without time zone,
    completion_time_minutes integer,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    updated_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT user_prompt_progress_status_check CHECK (((status)::text = ANY ((ARRAY['pending'::character varying, 'delivered'::character varying, 'completed'::character varying, 'skipped'::character varying])::text[])))
);


ALTER TABLE public.user_prompt_progress OWNER TO genie_admin;

--
-- Name: user_sessions; Type: TABLE; Schema: public; Owner: genie_admin
--

CREATE TABLE public.user_sessions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id character varying(255) NOT NULL,
    token_hash character varying(255) NOT NULL,
    ip_address character varying(45),
    user_agent text,
    created_at timestamp without time zone DEFAULT CURRENT_TIMESTAMP,
    expires_at timestamp without time zone,
    is_active boolean DEFAULT true
);


ALTER TABLE public.user_sessions OWNER TO genie_admin;

--
-- Name: weekly_stats; Type: VIEW; Schema: public; Owner: postgres
--

CREATE VIEW public.weekly_stats AS
 SELECT date_trunc('week'::text, gc.started_at) AS week,
    count(DISTINCT gc.user_id) AS active_users,
    count(DISTINCT gc.id) AS total_conversations,
    count(cm.id) AS total_messages
   FROM (public.genie_conversations gc
     LEFT JOIN public.conversation_messages cm ON ((gc.id = cm.conversation_id)))
  WHERE (gc.started_at IS NOT NULL)
  GROUP BY (date_trunc('week'::text, gc.started_at));


ALTER VIEW public.weekly_stats OWNER TO postgres;

--
-- Name: user_conversations_p0; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p0 FOR VALUES WITH (modulus 10, remainder 0);


--
-- Name: user_conversations_p1; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p1 FOR VALUES WITH (modulus 10, remainder 1);


--
-- Name: user_conversations_p2; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p2 FOR VALUES WITH (modulus 10, remainder 2);


--
-- Name: user_conversations_p3; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p3 FOR VALUES WITH (modulus 10, remainder 3);


--
-- Name: user_conversations_p4; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p4 FOR VALUES WITH (modulus 10, remainder 4);


--
-- Name: user_conversations_p5; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p5 FOR VALUES WITH (modulus 10, remainder 5);


--
-- Name: user_conversations_p6; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p6 FOR VALUES WITH (modulus 10, remainder 6);


--
-- Name: user_conversations_p7; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p7 FOR VALUES WITH (modulus 10, remainder 7);


--
-- Name: user_conversations_p8; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p8 FOR VALUES WITH (modulus 10, remainder 8);


--
-- Name: user_conversations_p9; Type: TABLE ATTACH; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition ATTACH PARTITION public.user_conversations_p9 FOR VALUES WITH (modulus 10, remainder 9);


--
-- Name: audit_log id; Type: DEFAULT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.audit_log ALTER COLUMN id SET DEFAULT nextval('public.audit_log_id_seq'::regclass);


--
-- Name: conversations id; Type: DEFAULT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.conversations ALTER COLUMN id SET DEFAULT nextval('public.conversations_id_seq'::regclass);


--
-- Name: user_nudges id; Type: DEFAULT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_nudges ALTER COLUMN id SET DEFAULT nextval('public.user_nudges_id_seq'::regclass);


--
-- Name: admin_notifications admin_notifications_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.admin_notifications
    ADD CONSTRAINT admin_notifications_pkey PRIMARY KEY (id);


--
-- Name: admin_users admin_users_email_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.admin_users
    ADD CONSTRAINT admin_users_email_key UNIQUE (email);


--
-- Name: admin_users admin_users_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.admin_users
    ADD CONSTRAINT admin_users_pkey PRIMARY KEY (id);


--
-- Name: audit_log audit_log_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.audit_log
    ADD CONSTRAINT audit_log_pkey PRIMARY KEY (id);


--
-- Name: baseline_content baseline_content_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.baseline_content
    ADD CONSTRAINT baseline_content_pkey PRIMARY KEY (id);


--
-- Name: content_ingestion_log content_ingestion_log_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.content_ingestion_log
    ADD CONSTRAINT content_ingestion_log_pkey PRIMARY KEY (file_id);


--
-- Name: conversation_messages conversation_messages_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.conversation_messages
    ADD CONSTRAINT conversation_messages_pkey PRIMARY KEY (id);


--
-- Name: conversations conversations_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.conversations
    ADD CONSTRAINT conversations_pkey PRIMARY KEY (id);


--
-- Name: curriculum_modules curriculum_modules_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.curriculum_modules
    ADD CONSTRAINT curriculum_modules_pkey PRIMARY KEY (id);


--
-- Name: curriculum_prompts curriculum_prompts_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.curriculum_prompts
    ADD CONSTRAINT curriculum_prompts_pkey PRIMARY KEY (id);


--
-- Name: user_data entityid_unique; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_data
    ADD CONSTRAINT entityid_unique UNIQUE (entityid);


--
-- Name: genie_conversations genie_conversations_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.genie_conversations
    ADD CONSTRAINT genie_conversations_pkey PRIMARY KEY (id);


--
-- Name: genie_instances genie_instances_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.genie_instances
    ADD CONSTRAINT genie_instances_pkey PRIMARY KEY (id);


--
-- Name: knowledge_base_content knowledge_base_content_document_id_version_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.knowledge_base_content
    ADD CONSTRAINT knowledge_base_content_document_id_version_key UNIQUE (document_id, version);


--
-- Name: knowledge_base_content knowledge_base_content_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.knowledge_base_content
    ADD CONSTRAINT knowledge_base_content_pkey PRIMARY KEY (id);


--
-- Name: knowledge_base knowledge_base_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.knowledge_base
    ADD CONSTRAINT knowledge_base_pkey PRIMARY KEY (id);


--
-- Name: leadership_content leadership_content_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.leadership_content
    ADD CONSTRAINT leadership_content_pkey PRIMARY KEY (content_id);


--
-- Name: leadership_daily_prompts leadership_daily_prompts_day_number_version_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.leadership_daily_prompts
    ADD CONSTRAINT leadership_daily_prompts_day_number_version_key UNIQUE (day_number, version);


--
-- Name: leadership_daily_prompts leadership_daily_prompts_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.leadership_daily_prompts
    ADD CONSTRAINT leadership_daily_prompts_pkey PRIMARY KEY (id);


--
-- Name: notification_log notification_log_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.notification_log
    ADD CONSTRAINT notification_log_pkey PRIMARY KEY (id);


--
-- Name: nudge_tracking nudge_tracking_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.nudge_tracking
    ADD CONSTRAINT nudge_tracking_pkey PRIMARY KEY (id);


--
-- Name: preference_change_log preference_change_log_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.preference_change_log
    ADD CONSTRAINT preference_change_log_pkey PRIMARY KEY (id);


--
-- Name: prompt_tracking prompt_tracking_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.prompt_tracking
    ADD CONSTRAINT prompt_tracking_pkey PRIMARY KEY (id);


--
-- Name: push_subscriptions push_subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.push_subscriptions
    ADD CONSTRAINT push_subscriptions_pkey PRIMARY KEY (id);


--
-- Name: push_subscriptions push_subscriptions_user_id_platform_endpoint_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.push_subscriptions
    ADD CONSTRAINT push_subscriptions_user_id_platform_endpoint_key UNIQUE (user_id, platform, endpoint);


--
-- Name: tenants tenants_domain_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.tenants
    ADD CONSTRAINT tenants_domain_key UNIQUE (domain);


--
-- Name: tenants tenants_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.tenants
    ADD CONSTRAINT tenants_pkey PRIMARY KEY (id);


--
-- Name: usage_metrics usage_metrics_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.usage_metrics
    ADD CONSTRAINT usage_metrics_pkey PRIMARY KEY (id);


--
-- Name: usage_metrics usage_metrics_user_id_date_platform_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.usage_metrics
    ADD CONSTRAINT usage_metrics_user_id_date_platform_key UNIQUE (user_id, date, platform);


--
-- Name: user_activity_log user_activity_log_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_activity_log
    ADD CONSTRAINT user_activity_log_pkey PRIMARY KEY (id);


--
-- Name: user_analytics user_analytics_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_analytics
    ADD CONSTRAINT user_analytics_pkey PRIMARY KEY (id);


--
-- Name: user_analytics user_analytics_user_id_date_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_analytics
    ADD CONSTRAINT user_analytics_user_id_date_key UNIQUE (user_id, date);


--
-- Name: user_conversations_partition user_conversations_partition_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_partition
    ADD CONSTRAINT user_conversations_partition_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p0 user_conversations_p0_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p0
    ADD CONSTRAINT user_conversations_p0_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p1 user_conversations_p1_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p1
    ADD CONSTRAINT user_conversations_p1_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p2 user_conversations_p2_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p2
    ADD CONSTRAINT user_conversations_p2_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p3 user_conversations_p3_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p3
    ADD CONSTRAINT user_conversations_p3_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p4 user_conversations_p4_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p4
    ADD CONSTRAINT user_conversations_p4_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p5 user_conversations_p5_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p5
    ADD CONSTRAINT user_conversations_p5_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p6 user_conversations_p6_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p6
    ADD CONSTRAINT user_conversations_p6_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p7 user_conversations_p7_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p7
    ADD CONSTRAINT user_conversations_p7_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p8 user_conversations_p8_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p8
    ADD CONSTRAINT user_conversations_p8_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_conversations_p9 user_conversations_p9_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_conversations_p9
    ADD CONSTRAINT user_conversations_p9_pkey PRIMARY KEY (user_id, conversation_id);


--
-- Name: user_data user_data_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_data
    ADD CONSTRAINT user_data_pkey PRIMARY KEY (id);


--
-- Name: user_data user_data_user_id_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_data
    ADD CONSTRAINT user_data_user_id_key UNIQUE (user_id);


--
-- Name: user_notifications user_notifications_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_notifications
    ADD CONSTRAINT user_notifications_pkey PRIMARY KEY (id);


--
-- Name: user_notifications user_notifications_user_id_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_notifications
    ADD CONSTRAINT user_notifications_user_id_key UNIQUE (user_id);


--
-- Name: user_nudges user_nudges_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_nudges
    ADD CONSTRAINT user_nudges_pkey PRIMARY KEY (id);


--
-- Name: user_preferences user_preferences_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_preferences
    ADD CONSTRAINT user_preferences_pkey PRIMARY KEY (id);


--
-- Name: user_preferences user_preferences_user_id_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_preferences
    ADD CONSTRAINT user_preferences_user_id_key UNIQUE (user_id);


--
-- Name: user_profiles user_profiles_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT user_profiles_pkey PRIMARY KEY (id);


--
-- Name: user_profiles user_profiles_user_id_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_profiles
    ADD CONSTRAINT user_profiles_user_id_key UNIQUE (user_id);


--
-- Name: user_prompt_deliveries user_prompt_deliveries_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_prompt_deliveries
    ADD CONSTRAINT user_prompt_deliveries_pkey PRIMARY KEY (id);


--
-- Name: user_prompt_deliveries user_prompt_deliveries_user_id_day_number_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_prompt_deliveries
    ADD CONSTRAINT user_prompt_deliveries_user_id_day_number_key UNIQUE (user_id, day_number);


--
-- Name: user_prompt_progress user_prompt_progress_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_prompt_progress
    ADD CONSTRAINT user_prompt_progress_pkey PRIMARY KEY (id);


--
-- Name: user_prompt_progress user_prompt_progress_user_id_day_number_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_prompt_progress
    ADD CONSTRAINT user_prompt_progress_user_id_day_number_key UNIQUE (user_id, day_number);


--
-- Name: user_responses user_responses_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_responses
    ADD CONSTRAINT user_responses_pkey PRIMARY KEY (id);


--
-- Name: user_sessions user_sessions_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_sessions
    ADD CONSTRAINT user_sessions_pkey PRIMARY KEY (id);


--
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- Name: users users_tenant_id_email_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_tenant_id_email_key UNIQUE (tenant_id, email);


--
-- Name: users users_tenant_id_tbb_user_id_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_tenant_id_tbb_user_id_key UNIQUE (tenant_id, tbb_user_id);


--
-- Name: users users_username_key; Type: CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_username_key UNIQUE (username);


--
-- Name: idx_baseline_content_created; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_baseline_content_created ON public.baseline_content USING btree (created_at DESC);


--
-- Name: idx_baseline_content_insights; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_baseline_content_insights ON public.baseline_content USING gin (insights);


--
-- Name: idx_baseline_content_metadata; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_baseline_content_metadata ON public.baseline_content USING gin (metadata);


--
-- Name: idx_conversations_conversation_id; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_conversations_conversation_id ON public.conversations USING btree (conversation_id);


--
-- Name: idx_conversations_user; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_conversations_user ON public.genie_conversations USING btree (user_id);


--
-- Name: idx_conversations_user_date; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_conversations_user_date ON public.genie_conversations USING btree (user_id, started_at DESC);


--
-- Name: idx_conversations_user_id; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_conversations_user_id ON public.conversations USING btree (user_id);


--
-- Name: idx_ingestion_category; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_ingestion_category ON public.content_ingestion_log USING btree (category);


--
-- Name: idx_knowledge_content_folder; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_knowledge_content_folder ON public.knowledge_base_content USING btree (source_folder, is_active);


--
-- Name: idx_knowledge_content_hash; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_knowledge_content_hash ON public.knowledge_base_content USING btree (content_hash);


--
-- Name: idx_leadership_content_category; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_leadership_content_category ON public.leadership_content USING btree (category);


--
-- Name: idx_leadership_content_day; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_leadership_content_day ON public.leadership_content USING btree (day_number);


--
-- Name: idx_leadership_content_type; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_leadership_content_type ON public.leadership_content USING btree (type);


--
-- Name: idx_leadership_prompts_day; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_leadership_prompts_day ON public.leadership_daily_prompts USING btree (day_number, is_active);


--
-- Name: idx_messages_conversation; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_messages_conversation ON public.conversation_messages USING btree (conversation_id);


--
-- Name: idx_messages_conversation_date; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_messages_conversation_date ON public.conversation_messages USING btree (conversation_id, created_at);


--
-- Name: idx_messages_created; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_messages_created ON public.conversation_messages USING btree (created_at);


--
-- Name: idx_notification_log_user_sent; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_notification_log_user_sent ON public.notification_log USING btree (user_id, sent_at DESC);


--
-- Name: idx_nudge_tracking_user_type; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_nudge_tracking_user_type ON public.nudge_tracking USING btree (user_id, nudge_type, sent_at);


--
-- Name: idx_prompts_day; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_prompts_day ON public.leadership_daily_prompts USING btree (day_number);


--
-- Name: idx_push_subs_user; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_push_subs_user ON public.push_subscriptions USING btree (user_id, is_active);


--
-- Name: idx_user_conv_created; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_conv_created ON ONLY public.user_conversations_partition USING btree (created_at);


--
-- Name: idx_user_conv_userid; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_conv_userid ON ONLY public.user_conversations_partition USING btree (user_id);


--
-- Name: idx_user_data_active; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_data_active ON public.user_data USING btree (is_active);


--
-- Name: idx_user_data_email; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_data_email ON public.user_data USING btree (email);


--
-- Name: idx_user_data_external_id; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE UNIQUE INDEX idx_user_data_external_id ON public.user_data USING btree (external_id);


--
-- Name: idx_user_data_user_id; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_data_user_id ON public.user_data USING btree (user_id);


--
-- Name: idx_user_notifications_user_id; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_notifications_user_id ON public.user_notifications USING btree (user_id);


--
-- Name: idx_user_nudges_responded; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_nudges_responded ON public.user_nudges USING btree (responded_at) WHERE (responded_at IS NOT NULL);


--
-- Name: idx_user_nudges_user_sent; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_nudges_user_sent ON public.user_nudges USING btree (user_id, sent_at DESC);


--
-- Name: idx_user_preferences_delivery; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_preferences_delivery ON public.user_preferences USING btree (prompt_delivery_time, timezone);


--
-- Name: idx_user_profiles_user; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_profiles_user ON public.user_profiles USING btree (user_id);


--
-- Name: idx_user_progress_status; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_user_progress_status ON public.user_prompt_progress USING btree (user_id, status, day_number);


--
-- Name: idx_users_email; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_users_email ON public.users USING btree (email);


--
-- Name: idx_users_tbb_id; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_users_tbb_id ON public.users USING btree (tbb_user_id);


--
-- Name: idx_users_tenant_email; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX idx_users_tenant_email ON public.users USING btree (tenant_id, email);


--
-- Name: user_conversations_p0_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p0_created_at_idx ON public.user_conversations_p0 USING btree (created_at);


--
-- Name: user_conversations_p0_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p0_user_id_idx ON public.user_conversations_p0 USING btree (user_id);


--
-- Name: user_conversations_p1_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p1_created_at_idx ON public.user_conversations_p1 USING btree (created_at);


--
-- Name: user_conversations_p1_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p1_user_id_idx ON public.user_conversations_p1 USING btree (user_id);


--
-- Name: user_conversations_p2_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p2_created_at_idx ON public.user_conversations_p2 USING btree (created_at);


--
-- Name: user_conversations_p2_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p2_user_id_idx ON public.user_conversations_p2 USING btree (user_id);


--
-- Name: user_conversations_p3_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p3_created_at_idx ON public.user_conversations_p3 USING btree (created_at);


--
-- Name: user_conversations_p3_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p3_user_id_idx ON public.user_conversations_p3 USING btree (user_id);


--
-- Name: user_conversations_p4_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p4_created_at_idx ON public.user_conversations_p4 USING btree (created_at);


--
-- Name: user_conversations_p4_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p4_user_id_idx ON public.user_conversations_p4 USING btree (user_id);


--
-- Name: user_conversations_p5_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p5_created_at_idx ON public.user_conversations_p5 USING btree (created_at);


--
-- Name: user_conversations_p5_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p5_user_id_idx ON public.user_conversations_p5 USING btree (user_id);


--
-- Name: user_conversations_p6_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p6_created_at_idx ON public.user_conversations_p6 USING btree (created_at);


--
-- Name: user_conversations_p6_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p6_user_id_idx ON public.user_conversations_p6 USING btree (user_id);


--
-- Name: user_conversations_p7_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p7_created_at_idx ON public.user_conversations_p7 USING btree (created_at);


--
-- Name: user_conversations_p7_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p7_user_id_idx ON public.user_conversations_p7 USING btree (user_id);


--
-- Name: user_conversations_p8_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p8_created_at_idx ON public.user_conversations_p8 USING btree (created_at);


--
-- Name: user_conversations_p8_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p8_user_id_idx ON public.user_conversations_p8 USING btree (user_id);


--
-- Name: user_conversations_p9_created_at_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p9_created_at_idx ON public.user_conversations_p9 USING btree (created_at);


--
-- Name: user_conversations_p9_user_id_idx; Type: INDEX; Schema: public; Owner: genie_admin
--

CREATE INDEX user_conversations_p9_user_id_idx ON public.user_conversations_p9 USING btree (user_id);


--
-- Name: user_conversations_p0_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p0_created_at_idx;


--
-- Name: user_conversations_p0_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p0_pkey;


--
-- Name: user_conversations_p0_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p0_user_id_idx;


--
-- Name: user_conversations_p1_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p1_created_at_idx;


--
-- Name: user_conversations_p1_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p1_pkey;


--
-- Name: user_conversations_p1_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p1_user_id_idx;


--
-- Name: user_conversations_p2_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p2_created_at_idx;


--
-- Name: user_conversations_p2_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p2_pkey;


--
-- Name: user_conversations_p2_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p2_user_id_idx;


--
-- Name: user_conversations_p3_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p3_created_at_idx;


--
-- Name: user_conversations_p3_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p3_pkey;


--
-- Name: user_conversations_p3_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p3_user_id_idx;


--
-- Name: user_conversations_p4_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p4_created_at_idx;


--
-- Name: user_conversations_p4_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p4_pkey;


--
-- Name: user_conversations_p4_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p4_user_id_idx;


--
-- Name: user_conversations_p5_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p5_created_at_idx;


--
-- Name: user_conversations_p5_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p5_pkey;


--
-- Name: user_conversations_p5_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p5_user_id_idx;


--
-- Name: user_conversations_p6_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p6_created_at_idx;


--
-- Name: user_conversations_p6_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p6_pkey;


--
-- Name: user_conversations_p6_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p6_user_id_idx;


--
-- Name: user_conversations_p7_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p7_created_at_idx;


--
-- Name: user_conversations_p7_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p7_pkey;


--
-- Name: user_conversations_p7_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p7_user_id_idx;


--
-- Name: user_conversations_p8_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p8_created_at_idx;


--
-- Name: user_conversations_p8_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p8_pkey;


--
-- Name: user_conversations_p8_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p8_user_id_idx;


--
-- Name: user_conversations_p9_created_at_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_created ATTACH PARTITION public.user_conversations_p9_created_at_idx;


--
-- Name: user_conversations_p9_pkey; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.user_conversations_partition_pkey ATTACH PARTITION public.user_conversations_p9_pkey;


--
-- Name: user_conversations_p9_user_id_idx; Type: INDEX ATTACH; Schema: public; Owner: genie_admin
--

ALTER INDEX public.idx_user_conv_userid ATTACH PARTITION public.user_conversations_p9_user_id_idx;


--
-- Name: conversation_messages conversation_messages_conversation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.conversation_messages
    ADD CONSTRAINT conversation_messages_conversation_id_fkey FOREIGN KEY (conversation_id) REFERENCES public.genie_conversations(id) ON DELETE CASCADE;


--
-- Name: curriculum_modules curriculum_modules_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.curriculum_modules
    ADD CONSTRAINT curriculum_modules_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id);


--
-- Name: curriculum_prompts curriculum_prompts_module_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.curriculum_prompts
    ADD CONSTRAINT curriculum_prompts_module_id_fkey FOREIGN KEY (module_id) REFERENCES public.curriculum_modules(id) ON DELETE CASCADE;


--
-- Name: genie_conversations genie_conversations_genie_instance_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.genie_conversations
    ADD CONSTRAINT genie_conversations_genie_instance_id_fkey FOREIGN KEY (genie_instance_id) REFERENCES public.genie_instances(id) ON DELETE CASCADE;


--
-- Name: genie_conversations genie_conversations_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.genie_conversations
    ADD CONSTRAINT genie_conversations_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: genie_instances genie_instances_owner_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.genie_instances
    ADD CONSTRAINT genie_instances_owner_user_id_fkey FOREIGN KEY (owner_user_id) REFERENCES public.users(id);


--
-- Name: genie_instances genie_instances_parent_genie_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.genie_instances
    ADD CONSTRAINT genie_instances_parent_genie_id_fkey FOREIGN KEY (parent_genie_id) REFERENCES public.genie_instances(id);


--
-- Name: genie_instances genie_instances_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.genie_instances
    ADD CONSTRAINT genie_instances_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: nudge_tracking nudge_tracking_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.nudge_tracking
    ADD CONSTRAINT nudge_tracking_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: prompt_tracking prompt_tracking_prompt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.prompt_tracking
    ADD CONSTRAINT prompt_tracking_prompt_id_fkey FOREIGN KEY (prompt_id) REFERENCES public.curriculum_prompts(id);


--
-- Name: usage_metrics usage_metrics_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.usage_metrics
    ADD CONSTRAINT usage_metrics_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);


--
-- Name: user_nudges user_nudges_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_nudges
    ADD CONSTRAINT user_nudges_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);


--
-- Name: user_preferences user_preferences_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_preferences
    ADD CONSTRAINT user_preferences_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: user_prompt_progress user_prompt_progress_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_prompt_progress
    ADD CONSTRAINT user_prompt_progress_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- Name: user_responses user_responses_conversation_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_responses
    ADD CONSTRAINT user_responses_conversation_id_fkey FOREIGN KEY (conversation_id) REFERENCES public.genie_conversations(id);


--
-- Name: user_responses user_responses_prompt_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.user_responses
    ADD CONSTRAINT user_responses_prompt_id_fkey FOREIGN KEY (prompt_id) REFERENCES public.curriculum_prompts(id) ON DELETE CASCADE;


--
-- Name: users users_tenant_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: genie_admin
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_tenant_id_fkey FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE;


--
-- Name: SCHEMA public; Type: ACL; Schema: -; Owner: pg_database_owner
--

GRANT ALL ON SCHEMA public TO genie_admin;


--
-- Name: FUNCTION armor(bytea); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.armor(bytea) TO genie_admin;


--
-- Name: FUNCTION armor(bytea, text[], text[]); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.armor(bytea, text[], text[]) TO genie_admin;


--
-- Name: FUNCTION crypt(text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.crypt(text, text) TO genie_admin;


--
-- Name: FUNCTION dearmor(text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.dearmor(text) TO genie_admin;


--
-- Name: FUNCTION decrypt(bytea, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.decrypt(bytea, bytea, text) TO genie_admin;


--
-- Name: FUNCTION decrypt_iv(bytea, bytea, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.decrypt_iv(bytea, bytea, bytea, text) TO genie_admin;


--
-- Name: FUNCTION digest(bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.digest(bytea, text) TO genie_admin;


--
-- Name: FUNCTION digest(text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.digest(text, text) TO genie_admin;


--
-- Name: FUNCTION encrypt(bytea, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.encrypt(bytea, bytea, text) TO genie_admin;


--
-- Name: FUNCTION encrypt_iv(bytea, bytea, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.encrypt_iv(bytea, bytea, bytea, text) TO genie_admin;


--
-- Name: FUNCTION gen_random_bytes(integer); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.gen_random_bytes(integer) TO genie_admin;


--
-- Name: FUNCTION gen_random_uuid(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.gen_random_uuid() TO genie_admin;


--
-- Name: FUNCTION gen_salt(text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.gen_salt(text) TO genie_admin;


--
-- Name: FUNCTION gen_salt(text, integer); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.gen_salt(text, integer) TO genie_admin;


--
-- Name: FUNCTION hmac(bytea, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.hmac(bytea, bytea, text) TO genie_admin;


--
-- Name: FUNCTION hmac(text, text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.hmac(text, text, text) TO genie_admin;


--
-- Name: FUNCTION pg_stat_statements(showtext boolean, OUT userid oid, OUT dbid oid, OUT toplevel boolean, OUT queryid bigint, OUT query text, OUT plans bigint, OUT total_plan_time double precision, OUT min_plan_time double precision, OUT max_plan_time double precision, OUT mean_plan_time double precision, OUT stddev_plan_time double precision, OUT calls bigint, OUT total_exec_time double precision, OUT min_exec_time double precision, OUT max_exec_time double precision, OUT mean_exec_time double precision, OUT stddev_exec_time double precision, OUT rows bigint, OUT shared_blks_hit bigint, OUT shared_blks_read bigint, OUT shared_blks_dirtied bigint, OUT shared_blks_written bigint, OUT local_blks_hit bigint, OUT local_blks_read bigint, OUT local_blks_dirtied bigint, OUT local_blks_written bigint, OUT temp_blks_read bigint, OUT temp_blks_written bigint, OUT blk_read_time double precision, OUT blk_write_time double precision, OUT temp_blk_read_time double precision, OUT temp_blk_write_time double precision, OUT wal_records bigint, OUT wal_fpi bigint, OUT wal_bytes numeric, OUT jit_functions bigint, OUT jit_generation_time double precision, OUT jit_inlining_count bigint, OUT jit_inlining_time double precision, OUT jit_optimization_count bigint, OUT jit_optimization_time double precision, OUT jit_emission_count bigint, OUT jit_emission_time double precision); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pg_stat_statements(showtext boolean, OUT userid oid, OUT dbid oid, OUT toplevel boolean, OUT queryid bigint, OUT query text, OUT plans bigint, OUT total_plan_time double precision, OUT min_plan_time double precision, OUT max_plan_time double precision, OUT mean_plan_time double precision, OUT stddev_plan_time double precision, OUT calls bigint, OUT total_exec_time double precision, OUT min_exec_time double precision, OUT max_exec_time double precision, OUT mean_exec_time double precision, OUT stddev_exec_time double precision, OUT rows bigint, OUT shared_blks_hit bigint, OUT shared_blks_read bigint, OUT shared_blks_dirtied bigint, OUT shared_blks_written bigint, OUT local_blks_hit bigint, OUT local_blks_read bigint, OUT local_blks_dirtied bigint, OUT local_blks_written bigint, OUT temp_blks_read bigint, OUT temp_blks_written bigint, OUT blk_read_time double precision, OUT blk_write_time double precision, OUT temp_blk_read_time double precision, OUT temp_blk_write_time double precision, OUT wal_records bigint, OUT wal_fpi bigint, OUT wal_bytes numeric, OUT jit_functions bigint, OUT jit_generation_time double precision, OUT jit_inlining_count bigint, OUT jit_inlining_time double precision, OUT jit_optimization_count bigint, OUT jit_optimization_time double precision, OUT jit_emission_count bigint, OUT jit_emission_time double precision) TO genie_admin;


--
-- Name: FUNCTION pg_stat_statements_info(OUT dealloc bigint, OUT stats_reset timestamp with time zone); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pg_stat_statements_info(OUT dealloc bigint, OUT stats_reset timestamp with time zone) TO genie_admin;


--
-- Name: FUNCTION pg_stat_statements_reset(userid oid, dbid oid, queryid bigint); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pg_stat_statements_reset(userid oid, dbid oid, queryid bigint) TO genie_admin;


--
-- Name: FUNCTION pgp_armor_headers(text, OUT key text, OUT value text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_armor_headers(text, OUT key text, OUT value text) TO genie_admin;


--
-- Name: FUNCTION pgp_key_id(bytea); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_key_id(bytea) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_decrypt(bytea, bytea); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_decrypt(bytea, bytea) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_decrypt(bytea, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_decrypt(bytea, bytea, text) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_decrypt(bytea, bytea, text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_decrypt(bytea, bytea, text, text) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_decrypt_bytea(bytea, bytea); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_decrypt_bytea(bytea, bytea) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_decrypt_bytea(bytea, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_decrypt_bytea(bytea, bytea, text) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_decrypt_bytea(bytea, bytea, text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_decrypt_bytea(bytea, bytea, text, text) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_encrypt(text, bytea); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_encrypt(text, bytea) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_encrypt(text, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_encrypt(text, bytea, text) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_encrypt_bytea(bytea, bytea); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_encrypt_bytea(bytea, bytea) TO genie_admin;


--
-- Name: FUNCTION pgp_pub_encrypt_bytea(bytea, bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_pub_encrypt_bytea(bytea, bytea, text) TO genie_admin;


--
-- Name: FUNCTION pgp_sym_decrypt(bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_sym_decrypt(bytea, text) TO genie_admin;


--
-- Name: FUNCTION pgp_sym_decrypt(bytea, text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_sym_decrypt(bytea, text, text) TO genie_admin;


--
-- Name: FUNCTION pgp_sym_decrypt_bytea(bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_sym_decrypt_bytea(bytea, text) TO genie_admin;


--
-- Name: FUNCTION pgp_sym_decrypt_bytea(bytea, text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_sym_decrypt_bytea(bytea, text, text) TO genie_admin;


--
-- Name: FUNCTION pgp_sym_encrypt(text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_sym_encrypt(text, text) TO genie_admin;


--
-- Name: FUNCTION pgp_sym_encrypt(text, text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_sym_encrypt(text, text, text) TO genie_admin;


--
-- Name: FUNCTION pgp_sym_encrypt_bytea(bytea, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_sym_encrypt_bytea(bytea, text) TO genie_admin;


--
-- Name: FUNCTION pgp_sym_encrypt_bytea(bytea, text, text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.pgp_sym_encrypt_bytea(bytea, text, text) TO genie_admin;


--
-- Name: FUNCTION uuid_generate_v1(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_generate_v1() TO genie_admin;


--
-- Name: FUNCTION uuid_generate_v1mc(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_generate_v1mc() TO genie_admin;


--
-- Name: FUNCTION uuid_generate_v3(namespace uuid, name text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_generate_v3(namespace uuid, name text) TO genie_admin;


--
-- Name: FUNCTION uuid_generate_v4(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_generate_v4() TO genie_admin;


--
-- Name: FUNCTION uuid_generate_v5(namespace uuid, name text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_generate_v5(namespace uuid, name text) TO genie_admin;


--
-- Name: FUNCTION uuid_nil(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_nil() TO genie_admin;


--
-- Name: FUNCTION uuid_ns_dns(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_ns_dns() TO genie_admin;


--
-- Name: FUNCTION uuid_ns_oid(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_ns_oid() TO genie_admin;


--
-- Name: FUNCTION uuid_ns_url(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_ns_url() TO genie_admin;


--
-- Name: FUNCTION uuid_ns_x500(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.uuid_ns_x500() TO genie_admin;


--
-- Name: TABLE daily_activity; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.daily_activity TO genie_admin;


--
-- Name: TABLE pg_stat_statements; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.pg_stat_statements TO genie_admin;


--
-- Name: TABLE pg_stat_statements_info; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.pg_stat_statements_info TO genie_admin;


--
-- Name: TABLE preference_change_log; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.preference_change_log TO genie_admin;


--
-- Name: TABLE prompt_completion_stats; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.prompt_completion_stats TO genie_admin;


--
-- Name: TABLE user_engagement_stats; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.user_engagement_stats TO genie_admin;


--
-- Name: TABLE weekly_stats; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.weekly_stats TO genie_admin;


--
-- Name: DEFAULT PRIVILEGES FOR SEQUENCES; Type: DEFAULT ACL; Schema: public; Owner: genie_admin
--

ALTER DEFAULT PRIVILEGES FOR ROLE genie_admin IN SCHEMA public GRANT ALL ON SEQUENCES TO genie_admin;


--
-- Name: DEFAULT PRIVILEGES FOR SEQUENCES; Type: DEFAULT ACL; Schema: public; Owner: postgres
--

ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON SEQUENCES TO genie_admin;


--
-- Name: DEFAULT PRIVILEGES FOR TABLES; Type: DEFAULT ACL; Schema: public; Owner: genie_admin
--

ALTER DEFAULT PRIVILEGES FOR ROLE genie_admin IN SCHEMA public GRANT ALL ON TABLES TO genie_admin;


--
-- Name: DEFAULT PRIVILEGES FOR TABLES; Type: DEFAULT ACL; Schema: public; Owner: postgres
--

ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON TABLES TO genie_admin;


--
-- PostgreSQL database dump complete
--

