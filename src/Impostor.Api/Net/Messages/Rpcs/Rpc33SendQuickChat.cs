using System;
using Impostor.Api.Innersloth;

namespace Impostor.Api.Net.Messages.Rpcs
{
    public static class Rpc33SendQuickChat
    {
        public static void Serialize(IMessageWriter writer, object data)
        {
            throw new NotImplementedException();
        }

        public static void Deserialize(IMessageReader reader, out string message)
        {
            var quickChatPhraseType = (QuickChatPhraseType)reader.ReadByte();
            switch (quickChatPhraseType)
            {
                case QuickChatPhraseType.Empty:
                    // QuickChatMenu.Logger.Error((object) "[QuickChatNetData] Empty is not a valid message type. Message body will be empty. This should not be happening!");
                case QuickChatPhraseType.PlayerId:
                    throw new NotImplementedException();
                case QuickChatPhraseType.SimplePhrase:
                    var stringName = (StringNames)reader.ReadUInt16();
                    message = stringName.ToString();
                    break;
                case QuickChatPhraseType.ComplexPhrase:
                    throw new NotImplementedException();
                default:
                    throw new NotImplementedException();
            }
        }
    }
}
