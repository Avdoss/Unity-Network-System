using System.Collections.Generic;
using Transport;
using System.Text;
using System;

namespace Network
{
    public enum MsgType : short
    {
        // sync scenes
        SyncSceneMessage = 0,
        SyncSceneAnswerMessage = 1,
        DesyncSceneMessage = 2,
        DesyncSceneAnswerMessage = 3,
        CreateObjectsMessage = 4,
        DeleteObjectsMessage = 5,
        UpdateObjectsFromOwnerMessage = 6,
        UpdateObjectsFromSlaveMessage = 7,
        // matchmaking
        CreateMatchQueryMessage = 20,
        CreateMatchResponseMessage = 21,
        BeginMatchQueryMessage = 22,
        BeginMatchResponseMessage = 23,
        EndMatchMessage = 24,
        DestroyMatchQueryMessage = 25,
        DestroyMatchResponseMessage = 26,
        ExecuteMatchQueryMessage = 27,
        ExecuteMatchResponseMessage = 28,
        CancelWaitingMatchQueryMessage = 29,
        BackToMatchQueryMessage = 30,
        // lobby
        UpdatePlayerMessage = 40,
        CreateCharacterQueryMessage = 41,
        DeleteCharacterQueryMessage = 42,
        ChangeCharacterQueryMessage = 43,
        UpdateCharactersMessage = 44,
        UpdateWardrobeMessage = 45,
        BuyClothItemQueryMessage = 46,
        SellClothItemQueryMessage = 47,
        ApplyClothItemQueryMessage = 48,
        ImproveCharacterPropertyQueryMessage = 49,
        MagicTowerRatingQueryMessage = 50,
        MagicTowerRatingResponseMessage = 51,
        //matches
        MagicTowerResultMessage = 200,
        MagicTowerDiedMessage = 201
        //PlayerSpawnMessage = 100
    }
    public interface INetworkMessage
    {
        void Serialize(WriteBuffer buffer);
        void Deserialize(ReadBuffer buffer);
    }

    public enum CONNECTION_ERROR : byte
    {
        BAD_VERSION = 4,
        BAD_LOGIN = 5,
        BAD_PASSWORD = 6,
        ALREADY_LOGGED = 7,
        REG_LOGIN_ALREADY_EXIST = 8,
        REG_EMAIL_ALREADY_EXIST = 9,
        REG_EMAIL_WRONG = 10,
        REG_AUTH_CODE_NEEDED = 11,
        PASS_RECOVERY_BAD_LOGIN = 12,
        PASS_RECOVERY_BAD_EMAIL = 13,
        PASS_RECOVERY_PLAYER_ONLINE = 14,
        PASS_RECOVERY_AUTH_CODE_NEEDED = 15,
        AUTH_CODE_SEND_ERROR = 16,
        AUTH_CODE_ERROR = 17,
        ATTEMPTS_LIMIT = 18,
        OPERATION_TIMEOUT = 19,
    }

    public enum CONNECTION_TYPE : byte
    {
        SERVER = 0,
        PLAYER = 1,
        MATCHMAKER = 2,
        MATCH = 3,
    }

    public enum MATCH_TYPE : byte
    {
        MAGIC_TOWER = 0
    }

    public enum CHARACTER_TYPE : byte
    {
        MAN = 0,
        WOMAN = 1,
    }

    public enum BODY_PART : byte
    {
        HEAD = 0,
        NECK = 1,
        BODY = 2,
        HANDS = 3,
        LEGS = 4,
        FEET = 5
    }

    public enum CHARACTER_PROPERTY : byte
    {
        SPEED = 0,
        ENDURANCE = 1,
        DEXTERITY = 2,
        STRENGTH = 3
    }

    public enum CREATE_MATCH_STATUS : byte
    {
        SUCCESSFULLY = 0,
        TIMEOUT = 1,
        MAX_MATCHES_LIMIT = 2,
        NOT_AVALIABLE_PORT = 3,
        MATCH_TYPE_NOT_SUPPORT = 4,
        AUTHORIZATION_ERROR = 5,
        CONNECTION_ERROR = 6,
        ERROR = 7,
    }

    public enum DESTROY_MATCH_STATUS : byte
    {
        NONE = 0,
        DESTROY_BY_MASTER = 1,
        TIMEOUT = 2,
        DISCONNECTED = 3,
        ERROR = 4,
    }

    public enum EXECUTE_MATCH_STATUS : short
    {
        WAIT = 0,
        BEGIN = 1,
        END = 2,
        WAIT_CANCELLED = 3,
        ERROR_MATCH_TERMINATED = 4,
        ERROR_WAIT_TIMEOUT = 5,
        ERROR_LITTLE_PLAYERS = 6,
        ERROR_MANY_PLAYERS = 7,
        ERROR_ALREADY_WAIT = 8,
        ERROR_UNKNOWN_MATCH_TYPE = 9,
        ERROR_CHARACTER_LOCKED = 10,
        ERROR_MATCH_CANCELLED = 11, // for back to match if match cancelled
    }

    [Serializable]
    public struct Version : IComparable<Version>
    {
        public string value;

        public Version(string value)
        {
            this.value = value;
        }

        public static bool operator ==(Version version1, Version version2)
        {
            return version1.value == version2.value;
        }

        public static bool operator !=(Version version1, Version version2)
        {
            return version1.value != version2.value;
        }

        public static bool operator >(Version version1, Version version2)
        {
            return version1.CompareTo(version2) > 0;
        }

        public static bool operator <(Version version1, Version version2)
        {
            return version1.CompareTo(version2) < 0;
        }

        public static bool operator >=(Version version1, Version version2)
        {
            return version1 == version2 || version1 > version2;
        }

        public static bool operator <=(Version version1, Version version2)
        {
            return version1 == version2 || version1 < version2;
        }

        public int CompareTo(Version other)
        {
            if (value == other.value)
                return 0;
            string[] words = value.Split('.');
            string[] other_words = other.value.Split('.');
            int count = Math.Min(words.Length, other_words.Length);
            for (int i = 0; i < count; i++)
            {
                int a = int.Parse(words[i]);
                int b = int.Parse(other_words[i]);
                if (a < b)
                    return -1;
                if (a > b)
                    return 1;
            }
            if (words.Length < other_words.Length)
                return -1;
            else
                return 1;
        }

        public override bool Equals(object obj)
        {
            if (obj is Version version)
                return value == version.value;
            else
                return false;
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public void Deserialize(ReadBuffer buffer)
        {
            int size = buffer.ReadInt();
            byte[] version_bytes = buffer.ReadArray(size);
            value = Encoding.UTF8.GetString(version_bytes);
        }

        public void Serialize(WriteBuffer buffer)
        {
            byte[] version_bytes = Encoding.UTF8.GetBytes(value);
            buffer.Write(version_bytes.Length);
            buffer.Write(version_bytes, 0, version_bytes.Length);
        }
    }

    public class NetworkPlayerInfo
    {
        public ulong uid;
        public string login;
        public string password;
        public float money;
        public NetworkCharacterInfo character;

        public virtual void Deserialize(ReadBuffer buffer)
        {
            uid = buffer.ReadULong();
            int size = buffer.ReadInt();
            byte[] login_bytes = buffer.ReadArray(size);
            login = Encoding.UTF8.GetString(login_bytes);

            size = buffer.ReadInt();
            byte[] password_bytes = buffer.ReadArray(size);
            password = Encoding.UTF8.GetString(password_bytes);

            money = buffer.ReadFloat();
            character = new NetworkCharacterInfo();
            character.Deserialize(buffer);
        }

        public virtual void Serialize(WriteBuffer buffer)
        {
            buffer.Write(uid);
            byte[] login_bytes = Encoding.UTF8.GetBytes(login);
            buffer.Write(login_bytes.Length);
            buffer.Write(login_bytes, 0, login_bytes.Length);

            byte[] password_bytes = Encoding.UTF8.GetBytes(password);
            buffer.Write(password_bytes.Length);
            buffer.Write(password_bytes, 0, password_bytes.Length);

            buffer.Write(money);
            character.Serialize(buffer);
        }
    }

    public class MagicTowerPlayerInfo : NetworkPlayerInfo
    {
        public ushort rating;
        public override void Deserialize(ReadBuffer buffer)
        {
            base.Deserialize(buffer);
            rating = buffer.ReadUShort();
        }

        public override void Serialize(WriteBuffer buffer)
        {
            base.Serialize(buffer);
            buffer.Write(rating);
        }
    }

    public class NetworkCharacterInfo
    {
        public static STATE_MASK ConvertBodyPartToUpdateMask(BODY_PART body_part)
        {
            if (body_part == BODY_PART.HEAD)
                return STATE_MASK.HEAD_UPDATE;
            if (body_part == BODY_PART.NECK)
                return STATE_MASK.NECK_UPDATE;
            if (body_part == BODY_PART.BODY)
                return STATE_MASK.BODY_UPDATE;
            if (body_part == BODY_PART.HANDS)
                return STATE_MASK.HANDS_UPDATE;
            if (body_part == BODY_PART.LEGS)
                return STATE_MASK.LEGS_UPDATE;
            return STATE_MASK.FEET_UPDATE;
        }

        public enum STATE_MASK : int
        {
            NAME_UPDATE = 0x00000001,
            TYPE_UPDATE = 0x00000002,
            HEAD_UPDATE = 0x00000004,
            NECK_UPDATE = 0x00000008,
            BODY_UPDATE = 0x00000010,
            HANDS_UPDATE = 0x00000020,
            LEGS_UPDATE = 0x00000040,
            FEET_UPDATE = 0x00000080,
            SPEED_UPDATE = 0x00000100,
            ENDURANCE_UPDATE = 0x00000200,
            DEXTERITY_UPDATE = 0x00000400,
            STRENGTH_UPDATE = 0x00000800,
            LOCKED_UPDATE = 0x00001000,
            LVL_UPDATE = 0x00002000,
            EXP_UPDATE = 0x00004000,
            POINTS_UPDATE = 0x00008000,
            LOCKED_VALUE = 0x00010000,
            UPDATE_ALL = 0x0000FFFF
        }

        public int status;
        public ulong uid;
        public string name;
        public CHARACTER_TYPE type;
        public ushort head;
        public ushort neck;
        public ushort body;
        public ushort hands;
        public ushort legs;
        public ushort feet;
        public float speed;
        public float endurance;
        public float dexterity;
        public float strength;
        public byte lvl;
        public float exp;
        public uint points;
        public bool locked;

        public NetworkCharacterInfo()
        {
            this.status = 0;
        }
        public bool IsUpdate
        {
            get { return (status & (int)(~STATE_MASK.LOCKED_VALUE)) != 0; }
            set { if (value) status |= (int)(~STATE_MASK.LOCKED_VALUE); else status &= (int)(STATE_MASK.LOCKED_VALUE); }
        }
        public bool IsLockedUpdate
        {
            get { return (status & (int)STATE_MASK.LOCKED_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.LOCKED_UPDATE; else status &= (int)(~STATE_MASK.LOCKED_UPDATE); }
        }
        public bool IsNameUpdate
        {
            get { return (status & (int)STATE_MASK.NAME_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.NAME_UPDATE; else status &= (int)(~STATE_MASK.NAME_UPDATE); }
        }
        public bool IsTypeUpdate
        {
            get { return (status & (int)STATE_MASK.TYPE_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.TYPE_UPDATE; else status &= (int)(~STATE_MASK.TYPE_UPDATE); }
        }
        public bool IsHeadUpdate
        {
            get { return (status & (int)STATE_MASK.HEAD_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.HEAD_UPDATE; else status &= (int)(~STATE_MASK.HEAD_UPDATE); }
        }
        public bool IsNeckUpdate
        {
            get { return (status & (int)STATE_MASK.NECK_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.NECK_UPDATE; else status &= (int)(~STATE_MASK.NECK_UPDATE); }
        }
        public bool IsBodyUpdate
        {
            get { return (status & (int)STATE_MASK.BODY_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.BODY_UPDATE; else status &= (int)(~STATE_MASK.BODY_UPDATE); }
        }
        public bool IsHandsUpdate
        {
            get { return (status & (int)STATE_MASK.HANDS_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.HANDS_UPDATE; else status &= (int)(~STATE_MASK.HANDS_UPDATE); }
        }
        public bool IsLegsUpdate
        {
            get { return (status & (int)STATE_MASK.LEGS_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.LEGS_UPDATE; else status &= (int)(~STATE_MASK.LEGS_UPDATE); }
        }
        public bool IsFootsUpdate
        {
            get { return (status & (int)STATE_MASK.FEET_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.FEET_UPDATE; else status &= (int)(~STATE_MASK.FEET_UPDATE); }
        }
        public bool IsSpeedUpdate
        {
            get { return (status & (int)STATE_MASK.SPEED_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.SPEED_UPDATE; else status &= (int)(~STATE_MASK.SPEED_UPDATE); }
        }
        public bool IsEnduranceUpdate
        {
            get { return (status & (int)STATE_MASK.ENDURANCE_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.ENDURANCE_UPDATE; else status &= (int)(~STATE_MASK.ENDURANCE_UPDATE); }
        }
        public bool IsDexterityUpdate
        {
            get { return (status & (int)STATE_MASK.DEXTERITY_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.DEXTERITY_UPDATE; else status &= (int)(~STATE_MASK.DEXTERITY_UPDATE); }
        }
        public bool IsStrengthUpdate
        {
            get { return (status & (int)STATE_MASK.STRENGTH_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.STRENGTH_UPDATE; else status &= (int)(~STATE_MASK.STRENGTH_UPDATE); }
        }
        public bool IsLVLUpdate
        {
            get { return (status & (int)STATE_MASK.LVL_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.LVL_UPDATE; else status &= (int)(~STATE_MASK.LVL_UPDATE); }
        }
        public bool IsExpUpdate
        {
            get { return (status & (int)STATE_MASK.EXP_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.EXP_UPDATE; else status &= (int)(~STATE_MASK.EXP_UPDATE); }
        }
        public bool IsPointsUpdate
        {
            get { return (status & (int)STATE_MASK.POINTS_UPDATE) != 0; }
            set { if (value) status |= (int)STATE_MASK.POINTS_UPDATE; else status &= (int)(~STATE_MASK.POINTS_UPDATE); }
        }
        private bool IsLocked
        {
            get { return (status & (int)STATE_MASK.LOCKED_VALUE) != 0; }
            set { if (value) status |= (int)STATE_MASK.LOCKED_VALUE; else status &= (int)(~STATE_MASK.LOCKED_VALUE); }
        }

        public void Serialize(WriteBuffer buffer)
        {
            if (IsLockedUpdate)
                IsLocked = locked;
            buffer.Write(status);
            buffer.Write(uid);
            if (IsNameUpdate)
            {
                byte[] name_bytes = Encoding.UTF8.GetBytes(name);
                buffer.Write(name_bytes.Length);
                buffer.Write(name_bytes, 0, name_bytes.Length);
            }
            if (IsTypeUpdate)
                buffer.Write((byte)type);
            if (IsHeadUpdate)
                buffer.Write(head);
            if (IsNeckUpdate)
                buffer.Write(neck);
            if (IsBodyUpdate)
                buffer.Write(body);
            if (IsHandsUpdate)
                buffer.Write(hands);
            if (IsLegsUpdate)
                buffer.Write(legs);
            if (IsFootsUpdate)
                buffer.Write(feet);
            if (IsSpeedUpdate)
                buffer.Write(speed);
            if (IsEnduranceUpdate)
                buffer.Write(endurance);
            if (IsDexterityUpdate)
                buffer.Write(dexterity);
            if (IsStrengthUpdate)
                buffer.Write(strength);
            if (IsLVLUpdate)
                buffer.Write(lvl);
            if (IsExpUpdate)
                buffer.Write(exp);
            if (IsPointsUpdate)
                buffer.Write(points);
        }
        public void Deserialize(ReadBuffer buffer)
        {
            status = buffer.ReadInt();
            uid = buffer.ReadULong();
            if (IsLockedUpdate)
                locked = IsLocked;
            if (IsNameUpdate)
            {
                int size = buffer.ReadInt();
                byte[] name_bytes = buffer.ReadArray(size);
                name = Encoding.UTF8.GetString(name_bytes);
            }
            if (IsTypeUpdate)
                type = (CHARACTER_TYPE)buffer.ReadByte();
            if (IsHeadUpdate)
                head = buffer.ReadUShort();
            if (IsNeckUpdate)
                neck = buffer.ReadUShort();
            if (IsBodyUpdate)
                body = buffer.ReadUShort();
            if (IsHandsUpdate)
                hands = buffer.ReadUShort();
            if (IsLegsUpdate)
                legs = buffer.ReadUShort();
            if (IsFootsUpdate)
                feet = buffer.ReadUShort();
            if (IsSpeedUpdate)
                speed = buffer.ReadFloat();
            if (IsEnduranceUpdate)
                endurance = buffer.ReadFloat();
            if (IsDexterityUpdate)
                dexterity = buffer.ReadFloat();
            if (IsStrengthUpdate)
                strength = buffer.ReadFloat();
            if (IsLVLUpdate)
                lvl = buffer.ReadByte();
            if (IsExpUpdate)
                exp = buffer.ReadFloat();
            if (IsPointsUpdate)
                points = buffer.ReadUInt();
        }
    }

    public struct NetworkMTRating
    {
        public string login;
        public uint count;
        public float rating;

        public void Deserialize(ReadBuffer buffer)
        {
            int size = buffer.ReadInt();
            byte[] login_bytes = buffer.ReadArray(size);
            login = Encoding.UTF8.GetString(login_bytes);
            count = buffer.ReadUInt();
            rating = buffer.ReadFloat();
        }

        public void Serialize(WriteBuffer buffer)
        {
            byte[] login_bytes = Encoding.UTF8.GetBytes(login);
            buffer.Write(login_bytes.Length);
            buffer.Write(login_bytes, 0, login_bytes.Length);
            buffer.Write(count);
            buffer.Write(rating);
        }
    }

    public class ConnectionMessage : INetworkMessage
    {
        public Version version;
        public CONNECTION_TYPE conn_type;
        public INetworkMessage conn_data;
        public void Deserialize(ReadBuffer buffer)
        {
            version = new Version();
            version.Deserialize(buffer);
            conn_type = (CONNECTION_TYPE)buffer.ReadByte();
            switch (conn_type)
            {
                case CONNECTION_TYPE.PLAYER:
                    conn_data = new ConnectionPlayerMessage();
                    conn_data.Deserialize(buffer);
                    break;
                case CONNECTION_TYPE.MATCHMAKER:
                    conn_data = new ConnectionMatchmakerMessage();
                    conn_data.Deserialize(buffer);
                    break;
                case CONNECTION_TYPE.MATCH:
                    conn_data = new ConnectionMatchMessage();
                    conn_data.Deserialize(buffer);
                    break;
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            version.Serialize(buffer);
            buffer.Write((byte)conn_type);
            conn_data.Serialize(buffer);
        }
    }

    public class ConnectionPlayerMessage : INetworkMessage
    {
        public enum PLAYER_CONNECTION_TYPE : byte
        {
            LOGIN = 0,
            REGISTRATION_BEGIN = 1,
            REGISTRATION_END = 2,
            PASSWORD_RECOVERY_BEGIN = 3,
            PASSWORD_RECOVERY_END = 4,
            REGISTRATION_AUTH_CODE_REPEAT = 5,
            PASSWORD_RECOVERY_AUTH_CODE_REPEAT = 6,
        }
        public PLAYER_CONNECTION_TYPE player_conn_type;
        public string login;
        //TODO must be sent encrypted
        public string password;
        public string email;
        public int auth_code;



        public void Deserialize(ReadBuffer buffer)
        {
            player_conn_type = (PLAYER_CONNECTION_TYPE)buffer.ReadByte();
            int size = buffer.ReadInt();
            byte[] login_bytes = buffer.ReadArray(size);
            login = Encoding.UTF8.GetString(login_bytes);

            if (player_conn_type == PLAYER_CONNECTION_TYPE.LOGIN ||
                player_conn_type == PLAYER_CONNECTION_TYPE.REGISTRATION_BEGIN ||
                player_conn_type == PLAYER_CONNECTION_TYPE.PASSWORD_RECOVERY_END)
            {
                size = buffer.ReadInt();
                byte[] password_bytes = buffer.ReadArray(size);
                password = Encoding.UTF8.GetString(password_bytes);
            }
            if (player_conn_type == PLAYER_CONNECTION_TYPE.REGISTRATION_BEGIN ||
                player_conn_type == PLAYER_CONNECTION_TYPE.PASSWORD_RECOVERY_BEGIN)
            {
                size = buffer.ReadInt();
                byte[] email_bytes = buffer.ReadArray(size);
                email = Encoding.UTF8.GetString(email_bytes);
            }
            if (player_conn_type == PLAYER_CONNECTION_TYPE.REGISTRATION_END ||
                player_conn_type == PLAYER_CONNECTION_TYPE.PASSWORD_RECOVERY_END)
                auth_code = buffer.ReadInt();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write((byte)player_conn_type);
            byte[] login_bytes = Encoding.UTF8.GetBytes(login);
            buffer.Write(login_bytes.Length);
            buffer.Write(login_bytes, 0, login_bytes.Length);

            if (player_conn_type == PLAYER_CONNECTION_TYPE.LOGIN ||
                player_conn_type == PLAYER_CONNECTION_TYPE.REGISTRATION_BEGIN ||
                player_conn_type == PLAYER_CONNECTION_TYPE.PASSWORD_RECOVERY_END)
            {
                byte[] password_bytes = Encoding.UTF8.GetBytes(password);
                buffer.Write(password_bytes.Length);
                buffer.Write(password_bytes, 0, password_bytes.Length);
            }
            if (player_conn_type == PLAYER_CONNECTION_TYPE.REGISTRATION_BEGIN ||
                player_conn_type == PLAYER_CONNECTION_TYPE.PASSWORD_RECOVERY_BEGIN)
            {
                byte[] email_bytes = Encoding.UTF8.GetBytes(email);
                buffer.Write(email_bytes.Length);
                buffer.Write(email_bytes, 0, email_bytes.Length);
            }
            if (player_conn_type == PLAYER_CONNECTION_TYPE.REGISTRATION_END ||
                player_conn_type == PLAYER_CONNECTION_TYPE.PASSWORD_RECOVERY_END)
                buffer.Write(auth_code);
        }
    }

    public class ConnectionMatchmakerMessage : INetworkMessage
    {
        public string login;
        //TODO must be sent encrypted
        public string password;
        public Dictionary<MATCH_TYPE, int> match_limits;

        public void Deserialize(ReadBuffer buffer)
        {
            int size = buffer.ReadInt();
            byte[] login_bytes = buffer.ReadArray(size);
            login = Encoding.UTF8.GetString(login_bytes);

            size = buffer.ReadInt();
            byte[] password_bytes = buffer.ReadArray(size);
            password = Encoding.UTF8.GetString(password_bytes);

            match_limits = new Dictionary<MATCH_TYPE, int>();
            int count = buffer.ReadInt();
            for (int i = 0; i < count; i++)
            {
                MATCH_TYPE key = (MATCH_TYPE)buffer.ReadByte();
                int value = buffer.ReadInt();
                match_limits[key] = value;
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            byte[] login_bytes = Encoding.UTF8.GetBytes(login);
            buffer.Write(login_bytes.Length);
            buffer.Write(login_bytes, 0, login_bytes.Length);

            byte[] password_bytes = Encoding.UTF8.GetBytes(password);
            buffer.Write(password_bytes.Length);
            buffer.Write(password_bytes, 0, password_bytes.Length);

            buffer.Write(match_limits.Count);
            foreach (MATCH_TYPE match_type in match_limits.Keys)
            {
                buffer.Write((byte)match_type);
                buffer.Write(match_limits[match_type]);
            }
        }
    }

    public class ConnectionMatchMessage : INetworkMessage
    {
        public string password;
        public void Deserialize(ReadBuffer buffer)
        {
            int size = buffer.ReadInt();
            byte[] password_bytes = buffer.ReadArray(size);
            password = Encoding.UTF8.GetString(password_bytes);
        }

        public void Serialize(WriteBuffer buffer)
        {
            byte[] password_bytes = Encoding.UTF8.GetBytes(password);
            buffer.Write(password_bytes.Length);
            buffer.Write(password_bytes, 0, password_bytes.Length);
        }
    }

    public class UpdatePlayerMessage : INetworkMessage
    {
        public enum STATE_MASK : short
        {
            MONEY_UPDATE = 0x0001,
            CHARACTERS_UPDATE = 0x0002,
            WARDROBE_UPDATE = 0x0004,
            UPDATE_ALL = 0x0007
        }

        public short status;
        public float money;
        public UpdateCharactersMessage updateCharactersMessage;
        public UpdateWardrobeMessage updateWardrobeMessage;

        public bool IsUpdate
        {
            get { return status != 0; }
            set { if (value) status |= (short)(~0); else status = 0; }
        }

        public bool IsMoneyUpdate
        {
            get { return (status & (short)STATE_MASK.MONEY_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.MONEY_UPDATE; else status &= (short)(~STATE_MASK.MONEY_UPDATE); }
        }

        public bool IsCharactersUpdate
        {
            get { return (status & (short)STATE_MASK.CHARACTERS_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.CHARACTERS_UPDATE; else status &= (short)(~STATE_MASK.CHARACTERS_UPDATE); }
        }

        public bool IsWardrobeUpdate
        {
            get { return (status & (short)STATE_MASK.WARDROBE_UPDATE) != 0; }
            set { if (value) status |= (short)STATE_MASK.WARDROBE_UPDATE; else status &= (short)(~STATE_MASK.WARDROBE_UPDATE); }
        }

        public void Deserialize(ReadBuffer buffer)
        {
            status = buffer.ReadShort();
            if (IsMoneyUpdate)
                money = buffer.ReadFloat();
            if (IsCharactersUpdate)
            {
                updateCharactersMessage = new UpdateCharactersMessage();
                updateCharactersMessage.Deserialize(buffer);
            }
            if (IsWardrobeUpdate)
            {
                updateWardrobeMessage = new UpdateWardrobeMessage();
                updateWardrobeMessage.Deserialize(buffer);
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(status);
            if (IsMoneyUpdate)
                buffer.Write(money);
            if (IsCharactersUpdate)
                updateCharactersMessage.Serialize(buffer);
            if (IsWardrobeUpdate)
                updateWardrobeMessage.Serialize(buffer);
        }
    }

    public class CreateCharacterQueryMessage : INetworkMessage
    {
        public CHARACTER_TYPE type;
        public string name;
        public void Deserialize(ReadBuffer buffer)
        {
            type = (CHARACTER_TYPE)buffer.ReadByte();
            int size = buffer.ReadInt();
            byte[] name_bytes = buffer.ReadArray(size);
            name = Encoding.UTF8.GetString(name_bytes);
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write((byte)type);
            byte[] name_bytes = Encoding.UTF8.GetBytes(name);
            buffer.Write(name_bytes.Length);
            buffer.Write(name_bytes, 0, name_bytes.Length);
        }
    }

    public class UpdateCharactersMessage : INetworkMessage
    {
        public enum STATE_MASK : byte
        {
            DELETED_UPDATE = 0x01,
            CHARACTERS_UPDATE = 0x02,
            SELECTED_UPDATE = 0x04,
        }

        public byte status;
        public List<ulong> deleted_characters;
        public List<NetworkCharacterInfo> update_characters;
        public ulong selected;

        public bool IsDeletedUpdate
        {
            get { return (status & (byte)STATE_MASK.DELETED_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.DELETED_UPDATE; else status &= (byte)(~STATE_MASK.DELETED_UPDATE); }
        }
        public bool IsCharactersUpdate
        {
            get { return (status & (byte)STATE_MASK.CHARACTERS_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.CHARACTERS_UPDATE; else status &= (byte)(~STATE_MASK.CHARACTERS_UPDATE); }
        }
        public bool IsSelectedUpdate
        {
            get { return (status & (byte)STATE_MASK.SELECTED_UPDATE) != 0; }
            set { if (value) status |= (byte)STATE_MASK.SELECTED_UPDATE; else status &= (byte)(~STATE_MASK.SELECTED_UPDATE); }
        }

        public void Deserialize(ReadBuffer buffer)
        {
            status = buffer.ReadByte();
            if (IsDeletedUpdate)
            {
                int count = buffer.ReadInt();
                deleted_characters = new List<ulong>();
                for (int i = 0; i < count; i++)
                {
                    ulong value = buffer.ReadULong();
                    deleted_characters.Add(value);
                }
            }
            if (IsCharactersUpdate)
            {
                int count = buffer.ReadInt();
                update_characters = new List<NetworkCharacterInfo>();
                for (int i = 0; i < count; i++)
                {
                    NetworkCharacterInfo info = new NetworkCharacterInfo();
                    info.Deserialize(buffer);
                    update_characters.Add(info);
                }
            }
            if (IsSelectedUpdate)
                selected = buffer.ReadULong();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(status);
            if (IsDeletedUpdate)
            {
                buffer.Write(deleted_characters.Count);
                foreach (ulong uid in deleted_characters)
                    buffer.Write(uid);
            }
            if (IsCharactersUpdate)
            {
                buffer.Write(update_characters.Count);
                foreach (NetworkCharacterInfo info in update_characters)
                    info.Serialize(buffer);
            }
            if (IsSelectedUpdate)
                buffer.Write(selected);
        }
    }

    public class DeleteCharacterQueryMessage : INetworkMessage
    {
        public ulong uid; // character uid
        public void Deserialize(ReadBuffer buffer)
        {
            uid = buffer.ReadULong();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(uid);
        }
    }

    public class ChangeCharacterQueryMessage : INetworkMessage
    {
        public ulong uid; // character uid
        public void Deserialize(ReadBuffer buffer)
        {
            uid = buffer.ReadULong();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(uid);
        }
    }

    public class BuyClothItemQueryMessage : INetworkMessage
    {
        public ulong character_uid; // cloth uid
        public ulong cloth_uid; // cloth uid
        public void Deserialize(ReadBuffer buffer)
        {
            character_uid = buffer.ReadULong();
            cloth_uid = buffer.ReadULong();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(character_uid);
            buffer.Write(cloth_uid);
        }
    }

    public class SellClothItemQueryMessage : INetworkMessage
    {
        public ulong uid; // cloth uid
        public void Deserialize(ReadBuffer buffer)
        {
            uid = buffer.ReadULong();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(uid);
        }
    }

    public class ApplyClothItemQueryMessage : INetworkMessage
    {
        public ulong character_uid;
        public ulong cloth_uid;
        public void Deserialize(ReadBuffer buffer)
        {
            character_uid = buffer.ReadULong();
            cloth_uid = buffer.ReadULong();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(character_uid);
            buffer.Write(cloth_uid);
        }
    }

    public class UpdateWardrobeMessage : INetworkMessage
    {
        public Dictionary<ulong, uint> update_items;
        public void Deserialize(ReadBuffer buffer)
        {

            int count = buffer.ReadInt();
            update_items = new Dictionary<ulong, uint>(count);
            for (int i = 0; i < count; i++)
            {
                ulong key = buffer.ReadULong();
                uint value = buffer.ReadUInt();
                update_items.Add(key, value);
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(update_items.Count);
            foreach (var item in update_items)
            {
                buffer.Write(item.Key);
                buffer.Write(item.Value);
            }
        }
    }

    public class ImproveCharacterPropertyQueryMessage : INetworkMessage
    {
        public ulong character_uid;
        public CHARACTER_PROPERTY property;
        public void Deserialize(ReadBuffer buffer)
        {
            character_uid = buffer.ReadULong();
            property = (CHARACTER_PROPERTY)buffer.ReadByte();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(character_uid);
            buffer.Write((byte)property);
        }
    }

    public class MagicTowerRatingQueryMessage : INetworkMessage
    {
        public string login;
        public uint before_rows;
        public uint after_rows;
        public void Deserialize(ReadBuffer buffer)
        {
            int size = buffer.ReadInt();
            byte[] login_bytes = buffer.ReadArray(size);
            login = Encoding.UTF8.GetString(login_bytes);
            before_rows = buffer.ReadUInt();
            after_rows = buffer.ReadUInt();
        }

        public void Serialize(WriteBuffer buffer)
        {
            byte[] login_bytes = Encoding.UTF8.GetBytes(login);
            buffer.Write(login_bytes.Length);
            buffer.Write(login_bytes, 0, login_bytes.Length);
            buffer.Write(before_rows);
            buffer.Write(after_rows);
        }
    }

    public class MagicTowerRatingResponseMessage : INetworkMessage
    {
        public int first_row_rank;
        public int player_row_index;
        public int max_rank;
        public List<NetworkMTRating> rows;
        public void Deserialize(ReadBuffer buffer)
        {
            first_row_rank = buffer.ReadInt();
            player_row_index = buffer.ReadInt();
            max_rank = buffer.ReadInt();
            int size = buffer.ReadInt();
            rows = new List<NetworkMTRating>(size);
            for (int i = 0; i < size; i++)
            {
                NetworkMTRating row = new NetworkMTRating();
                row.Deserialize(buffer);
                rows.Add(row);
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(first_row_rank);
            buffer.Write(player_row_index);
            buffer.Write(max_rank);
            buffer.Write(rows.Count);
            foreach (NetworkMTRating row in rows)
                row.Serialize(buffer);
        }
    }

    public class CreateMatchQueryMessage : INetworkMessage
    {
        public MATCH_TYPE match_type;
        public void Deserialize(ReadBuffer buffer)
        {
            match_type = (MATCH_TYPE)buffer.ReadByte();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write((byte)match_type);
        }
    }

    public class CreateMatchResponseMessage : INetworkMessage
    {
        public MATCH_TYPE match_type;
        public CREATE_MATCH_STATUS status;
        public int match_id;
        public ushort port;
        public void Deserialize(ReadBuffer buffer)
        {
            match_type = (MATCH_TYPE)buffer.ReadByte();
            status = (CREATE_MATCH_STATUS)buffer.ReadByte();
            if (status == CREATE_MATCH_STATUS.SUCCESSFULLY)
            {
                match_id = buffer.ReadInt();
                port = buffer.ReadUShort();
            }
            else
            {
                match_id = -1;
                port = 0;
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write((byte)match_type);
            buffer.Write((byte)status);
            if (status == CREATE_MATCH_STATUS.SUCCESSFULLY)
            {
                buffer.Write(match_id);
                buffer.Write(port);
            }
        }
    }

    public class BeginMatchQueryMessage<T> : INetworkMessage where T : NetworkPlayerInfo, new()
    {
        public int match_id;
        public List<NetworkPlayerInfo> players_info;
        public void Deserialize(ReadBuffer buffer)
        {
            match_id = buffer.ReadInt();
            int count = buffer.ReadInt();
            players_info = new List<NetworkPlayerInfo>();
            for (int i = 0; i < count; i++)
            {
                T p_info = new T();
                p_info.Deserialize(buffer);
                players_info.Add(p_info);
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(match_id);
            buffer.Write(players_info.Count);
            foreach (NetworkPlayerInfo p_info in players_info)
                p_info.Serialize(buffer);
        }
    }

    public class BeginMatchResponseMessage : INetworkMessage
    {
        public int match_id;
        public void Deserialize(ReadBuffer buffer)
        {
            match_id = buffer.ReadInt();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(match_id);
        }
    }

    public class EndMatchMessage : INetworkMessage
    {
        public int match_id;
        public Dictionary<ulong, NetworkPlayerInfo> players_info; // key: uid
        public void Deserialize(ReadBuffer buffer)
        {
            match_id = buffer.ReadInt();
            int count = buffer.ReadInt();
            for (int i = 0; i < count; i++)
            {
                ulong uid = buffer.ReadULong();
                players_info[uid].Deserialize(buffer);
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(match_id);
            buffer.Write(players_info.Count);
            foreach (ulong uid in players_info.Keys)
            {
                buffer.Write(uid);
                players_info[uid].Serialize(buffer);
            }
        }

    }

    public class DestroyMatchQueryMessage : INetworkMessage
    {
        public int match_id;
        public void Deserialize(ReadBuffer buffer)
        {
            match_id = buffer.ReadInt();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(match_id);
        }
    }

    public class DestroyMatchResponseMessage : INetworkMessage
    {
        public int match_id;
        public DESTROY_MATCH_STATUS status;

        public void Deserialize(ReadBuffer buffer)
        {
            match_id = buffer.ReadInt();
            status = (DESTROY_MATCH_STATUS)buffer.ReadByte();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write(match_id);
            buffer.Write((byte)status);
        }
    }

    public class ExecuteMatchQueryMessage : INetworkMessage
    {
        public MATCH_TYPE match_type;
        public void Deserialize(ReadBuffer buffer)
        {
            match_type = (MATCH_TYPE)buffer.ReadByte();
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write((byte)match_type);
        }
    }

    public class ExecuteMatchResponseMessage : INetworkMessage
    {
        public MATCH_TYPE match_type;
        public EXECUTE_MATCH_STATUS status;
        public string match_ip;
        public ushort match_port;
        public int queue_length = -1;
        public int queue_pos = -1;

        public void Deserialize(ReadBuffer buffer)
        {
            match_type = (MATCH_TYPE)buffer.ReadByte();
            status = (EXECUTE_MATCH_STATUS)buffer.ReadShort();
            if (status == EXECUTE_MATCH_STATUS.BEGIN)
            {
                int size = buffer.ReadInt();
                byte[] match_ip_bytes = buffer.ReadArray(size);
                match_ip = Encoding.UTF8.GetString(match_ip_bytes);
                match_port = buffer.ReadUShort();
            }
            else if (status == EXECUTE_MATCH_STATUS.WAIT)
            {
                queue_length = buffer.ReadInt();
                queue_pos = buffer.ReadInt();
            }
        }

        public void Serialize(WriteBuffer buffer)
        {
            buffer.Write((byte)match_type);
            buffer.Write((short)status);
            if (status == EXECUTE_MATCH_STATUS.BEGIN)
            {
                byte[] match_ip_bytes = Encoding.UTF8.GetBytes(match_ip);
                buffer.Write(match_ip_bytes.Length);
                buffer.Write(match_ip_bytes, 0, match_ip_bytes.Length);
                buffer.Write(match_port);
            }
            else if (status == EXECUTE_MATCH_STATUS.WAIT)
            {
                buffer.Write(queue_length);
                buffer.Write(queue_pos);
            }
        }
    }

    public class CancelWaitingMatchQueryMessage : INetworkMessage
    {
        public void Deserialize(ReadBuffer buffer)
        {
        }

        public void Serialize(WriteBuffer buffer)
        {
        }
    }

    public class BackToMatchQueryMessage : INetworkMessage
    {
        public void Deserialize(ReadBuffer buffer)
        {

        }

        public void Serialize(WriteBuffer buffer)
        {

        }
    }
}