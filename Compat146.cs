using System.IO;
using System.Text;
using Terraria;
using Terraria.DataStructures;
using TerrariaApi.Server;
using TShockAPI;

namespace Compat146
{
	[ApiVersion(2, 1)]
	public class Compat146Plugin : TerrariaPlugin
	{
		private const string NewVersion = "Terraria329";
		private const string ServerVersion = "Terraria326";

		private static readonly bool[] IsNewClient = new bool[256];
		private static readonly short[] LastAnimationMax = new short[256];

		public override string Name => "Compat146";
		public override string Author => "isawicca";
		public override string Description => "Lets Terraria 1.4.6 clients join a 1.4.5.8 TShock server.";
		public override Version Version => new Version(1, 0, 0);

		public Compat146Plugin(Main game) : base(game)
		{
			Order = -10;
		}

		public override void Initialize()
		{
			ServerApi.Hooks.NetGetData.Register(this, OnGetData, int.MaxValue);
			ServerApi.Hooks.NetSendBytes.Register(this, OnSendBytes, int.MaxValue);
			ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
				ServerApi.Hooks.NetSendBytes.Deregister(this, OnSendBytes);
				ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
			}
			base.Dispose(disposing);
		}

		private static void OnLeave(LeaveEventArgs args)
		{
			if (args.Who >= 0 && args.Who < IsNewClient.Length)
			{
				IsNewClient[args.Who] = false;
				LastAnimationMax[args.Who] = 0;
			}
		}

		private static void OnGetData(GetDataEventArgs args)
		{
			if (args.Handled)
				return;

			int who = args.Msg.whoAmI;
			if (args.MsgID == PacketTypes.PlayerAnimation)
			{
				if (IsNewClient[who] && args.Length - 1 >= 9)
					LastAnimationMax[who] = BitConverter.ToInt16(args.Msg.readBuffer, args.Index + 7);
				return;
			}
			if (args.MsgID != PacketTypes.ConnectRequest)
				return;

			byte[] buffer = args.Msg.readBuffer;
			int start = args.Index;
			int length = args.Length - 1;
			if (length < 1 + NewVersion.Length || buffer[start] != NewVersion.Length)
			{
				IsNewClient[who] = false;
				return;
			}

			string version = Encoding.ASCII.GetString(buffer, start + 1, NewVersion.Length);
			if (version == NewVersion)
			{
				Encoding.ASCII.GetBytes(ServerVersion, 0, ServerVersion.Length, buffer, start + 1);
				IsNewClient[who] = true;
				TShock.Log.ConsoleInfo($"[Compat146] Slot {who} is a Terraria 1.4.6 client.");
			}
			else
			{
				IsNewClient[who] = false;
			}
		}

		private static void OnSendBytes(SendBytesEventArgs args)
		{
			if (args.Handled || args.Socket == null)
				return;

			int who = args.Socket.Id;
			if (who < 0 || who >= IsNewClient.Length || !IsNewClient[who])
				return;

			if (!NeedsTranslation(args.Buffer, args.Offset, args.Count))
				return;

			byte[] translated;
			try
			{
				translated = Translate(args.Buffer, args.Offset, args.Count);
			}
			catch (Exception ex)
			{
				TShock.Log.ConsoleError($"[Compat146] Could not translate packet for slot {who}: {ex.Message}");
				return;
			}

			args.Handled = true;
			try
			{
				args.Socket.Socket?.AsyncSend(translated, 0, translated.Length, args.Socket.ServerWriteCallBack);
			}
			catch
			{
			}
		}

		private static bool IsTranslated(byte id) => id == 41 || id == 88 || id == 91 || id == 117;

		private static bool NeedsTranslation(byte[] buffer, int offset, int count)
		{
			int position = offset;
			int end = offset + count;
			while (position + 3 <= end)
			{
				int length = BitConverter.ToUInt16(buffer, position);
				if (length < 3)
					return false;
				if (IsTranslated(buffer[position + 2]))
					return true;
				position += length;
			}
			return false;
		}

		private static byte[] Translate(byte[] buffer, int offset, int count)
		{
			using var output = new MemoryStream(count + 16);
			int position = offset;
			int end = offset + count;
			while (position + 3 <= end)
			{
				int length = BitConverter.ToUInt16(buffer, position);
				if (length < 3 || position + length > end)
				{
					output.Write(buffer, position, end - position);
					break;
				}

				byte id = buffer[position + 2];
				byte[] body = new byte[length - 3];
				Array.Copy(buffer, position + 3, body, 0, body.Length);
				byte[] newBody = id switch
				{
					41 => TranslateItemAnimation(body),
					88 => TranslateItemTweaker(body),
					91 => TranslateEmoteBubble(body),
					117 => TranslatePlayerHurt(body),
					_ => body
				};

				int newLength = newBody.Length + 3;
				output.WriteByte((byte)(newLength & 0xFF));
				output.WriteByte((byte)(newLength >> 8));
				output.WriteByte(id);
				output.Write(newBody, 0, newBody.Length);
				position += length;
			}
			if (position < end)
				output.Write(buffer, position, end - position);
			return output.ToArray();
		}

		private static byte[] TranslateItemAnimation(byte[] body)
		{
			if (body.Length != 7)
				return body;
			int player = body[0];
			short animation = BitConverter.ToInt16(body, 5);
			short animationMax = player < LastAnimationMax.Length ? LastAnimationMax[player] : (short)0;
			if (animationMax <= 0 && player < Main.player.Length && Main.player[player] != null)
				animationMax = (short)Main.player[player].itemAnimationMax;
			if (animationMax < animation)
				animationMax = animation;
			byte[] result = new byte[9];
			Array.Copy(body, result, 7);
			BitConverter.GetBytes(animationMax).CopyTo(result, 7);
			return result;
		}

		private static byte[] TranslatePlayerHurt(byte[] body)
		{
			using var reader = new BinaryReader(new MemoryStream(body));
			reader.ReadByte();
			PlayerDeathReason.FromReader(reader);
			reader.ReadInt16();
			reader.ReadByte();
			int flagsIndex = (int)reader.BaseStream.Position;
			if (flagsIndex >= body.Length)
				return body;
			byte[] result = (byte[])body.Clone();
			result[flagsIndex] |= 1 << 2;
			return result;
		}

		private static byte[] TranslateItemTweaker(byte[] body)
		{
			using var reader = new BinaryReader(new MemoryStream(body));
			using var output = new MemoryStream(body.Length);
			using var writer = new BinaryWriter(output);

			writer.Write(reader.ReadInt16());
			BitsByte flags = reader.ReadByte();
			writer.Write(flags);
			if (flags[0]) writer.Write(reader.ReadUInt32());
			if (flags[1]) writer.Write(reader.ReadUInt16());
			if (flags[2]) writer.Write(reader.ReadSingle());
			if (flags[3]) writer.Write(reader.ReadUInt16());
			if (flags[4]) writer.Write(reader.ReadUInt16());
			if (flags[5]) writer.Write(reader.ReadInt16());
			if (flags[6]) writer.Write(reader.ReadSingle());
			if (flags[7])
			{
				BitsByte flags2 = reader.ReadByte();
				if (flags2[0]) reader.ReadUInt16();
				if (flags2[1]) reader.ReadUInt16();
				BitsByte newFlags2 = flags2;
				newFlags2[0] = false;
				newFlags2[1] = false;
				writer.Write(newFlags2);
				if (flags2[2]) writer.Write(reader.ReadSingle());
				if (flags2[3]) writer.Write(reader.ReadInt16());
				if (flags2[4]) writer.Write(reader.ReadInt16());
				if (flags2[5]) writer.Write(reader.ReadBoolean());
			}
			writer.Flush();
			return output.ToArray();
		}

		private static byte[] TranslateEmoteBubble(byte[] body)
		{
			using var reader = new BinaryReader(new MemoryStream(body));
			using var output = new MemoryStream(body.Length + 4);
			using var writer = new BinaryWriter(output);

			writer.Write(reader.ReadInt32());
			byte anchorType = reader.ReadByte();
			if (anchorType == 255)
			{
				writer.Write(anchorType);
				writer.Flush();
				return output.ToArray();
			}

			int meta = reader.ReadUInt16();
			ushort lifeTime = reader.ReadUInt16();
			byte emote = reader.ReadByte();

			switch (anchorType)
			{
				case 0:
					writer.Write((byte)0);
					writer.Write((ushort)meta);
					break;
				case 1:
					writer.Write((byte)1);
					writer.Write((byte)meta);
					break;
				case 2 when meta < Main.projectile.Length && Main.projectile[meta] != null:
					writer.Write((byte)2);
					writer.Write((int)Main.projectile[meta].key);
					break;
				default:
					writer.Write((byte)3);
					break;
			}
			writer.Write(lifeTime);
			writer.Write(emote);
			writer.Flush();
			return output.ToArray();
		}
	}
}
