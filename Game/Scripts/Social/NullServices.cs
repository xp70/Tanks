﻿using CryEngine;

namespace CryGameCode.Social.Null
{
	public class NullChatService : NullService, ISocialChat
	{
		public NullChatService(NullAuth auth)
			: base(auth)
		{
		}

		public void Send(string roomId, string message)
		{
			Log("Outgoing chat message to {0}: {1}", roomId, message);
		}

		public string CreateRoom()
		{
			return "nullroom";
		}
	}

	public class NullGroupService : NullService, ISocialGroup
	{
		private GroupInfo m_groupInfo;

		public NullGroupService(NullAuth auth)
			: base(auth)
		{
			m_groupInfo = new GroupInfo(0);
		}

		public GroupInfo CurrentGroup { get { return m_groupInfo; } }
	}
}
