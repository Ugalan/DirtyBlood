using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml;

namespace Env.Net.FRM
{
    public class HvHelper
    {
        string _revInfo;
        Socket clientSocket;
        CountdownEvent latch = new CountdownEvent(1);
        IPEndPoint _ip;
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="ipAddr">IP地址</param>
        /// <param name="port">端口</param>
        public HvHelper(string ipAddr, int port)
        {
            _ip = new IPEndPoint(IPAddress.Parse(ipAddr), port);
        }

        /// <summary>
        /// 发送命令
        /// </summary>
        /// <param name="cmd">命令行</param>
        /// <param name="byteLen">获取到的byte数组长度，不包括后面的0</param>
        /// <returns></returns>
        public byte[] Send4Bys(string cmd, out int byteLen)
        {
            byteLen = 0;
            byte[] buffer = new byte[1024 * 1024]; // 1M
            try
            {
                // 《Android工具HierarchyViewer 代码导读》 https://blog.csdn.net/liguangzhenghi/article/details/8363911
                // Forword端口。就是把Android设备上的4939端口映射到PC的某端口上，这样，向PC的该端口号发包都会转发到Android设备的4939端口上
                // adb -s 127.0.0.1:21503 forward tcp:4939 tcp:4939
                // LIST -> 获取活动的Activity（Window）
                // DUMP ffffffff -> 获取Activity的控件树 
                // CAPTURE 4507aa28 android.widget.FrameLayout@44edba90 -> 截图
                System.Diagnostics.Stopwatch oTime = new System.Diagnostics.Stopwatch();
                clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                clientSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 3000); // 超时时间
                clientSocket.Connect(_ip);

                // Thread th = new Thread(Recive);
                // th.IsBackground = true; // 后台线程随着主线程退出
                // th.Start(clientSocket);
                // clientSocket.Send(Encoding.UTF8.GetBytes($"{cmd}\n"));
                // latch.Wait(); // 等待线程执行结束
                oTime.Start();
                clientSocket.Send(Encoding.UTF8.GetBytes($"{cmd}\n"));
                while (15000 > oTime.ElapsedMilliseconds)
                {
                    try
                    {
                        int length = clientSocket.Receive(buffer, byteLen, buffer.Length- byteLen, SocketFlags.None);
                        if (length > 0)
                        {
                            string temp = Encoding.UTF8.GetString(buffer, byteLen, length); // 待优化：此处导致内存偏高，
                            byteLen += length;
                            if (temp.TrimEnd(new char[] { '\n', ' ', '.'}).EndsWith("DONE")) // "DONE."对应68 79 78 69 46
                                break;
                        }
                    }
                    catch (Exception ex)
                    { }
                }
            }
            catch (Exception ex)
            { }

            // clientSocket.Shutdown(SocketShutdown.Both);
            // clientSocket.Close();
            // latch.Signal();
            return buffer;
        }

        /// <summary>
        /// 发送命令
        /// </summary>
        /// <param name="cmd"></param>
        /// <returns></returns>
        public string Send4Str(string cmd)
        {
            var buffer = Send4Bys(cmd, out int byteLen);
            return Encoding.UTF8.GetString(buffer, 0, byteLen);
        }

        private void Recive(object o)
        {
            _revInfo = null;
            Socket socketSend = o as Socket;
        }

        /// <summary>
        /// 获取所有Window信息
        /// </summary>
        /// <returns></returns>
        public string DumpAllWindowInfo()
        {
            return Send4Str("LIST");
        }

        /// <summary>
        /// 获取当前Window信息
        /// </summary>
        /// <returns></returns>
        public string DumpCurWindowAllNodeInfo()
        {
            return Send4Str("DUMP ffffffff");
        }

        /// <summary>
        /// 获取单个Window的所有节点信息
        /// </summary>
        /// <param name="hashCode"></param>
        /// <returns></returns>
        public string DumpWindowAllNodeInfo(string hashCode)
        {
            return Send4Str($"DUMP {hashCode}");
        }

        /// <summary>
        /// 节点截图
        /// </summary>
        /// <param name="windowHashCode"></param>
        /// <param name="ctrlClassName"></param>
        /// <param name="ctrlClassHashCode"></param>
        /// <returns></returns>
        public byte[] CaptureNode(string windowHashCode, string ctrlClassName, string ctrlClassHashCode)
        {
            return Send4Bys($"CAPTURE {windowHashCode} {ctrlClassName}@{ctrlClassHashCode}", out int byteLen);
        }

        /// <summary>
        /// 端口是否被占用
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public static bool IsPortInUse(int port)
        {
            bool isUse = false;
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] ipEndPoints = ipProperties.GetActiveTcpListeners();
            foreach (IPEndPoint endPoint in ipEndPoints)
            {
                if (endPoint.Port == port)
                {
                    isUse = true;
                    break;
                }
            }
            return isUse;
        }

        /// <summary>
        /// 节点信息转换为XML
        /// </summary>
        /// <param name="nodeInfo"></param>
        public void DumpNodeToXml(string nodeInfo)
        {
            // string[] nodeArray = File.ReadAllLines(@"D:\dumpnode.txt");
            string[] nodeArray = nodeInfo.Replace("DONE.", "").Split(new string[] { "\n"}, StringSplitOptions.RemoveEmptyEntries).ToArray();
            XmlDocument doc = new XmlDocument();
            doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", ""));
            XmlElement sub = doc.CreateElement("Node");
            SetNodeProperty(sub, nodeArray.First());
            doc.AppendChild(sub);
            DumpNodeToXml(nodeArray, 0, doc, sub);
            // doc.Save(@"D:\xmlnode.xml");
        }

        /// <summary>
        /// 递归节点信息转换为XML
        /// </summary>
        /// <param name="nodeArray"></param>
        /// <param name="lineNum"></param>
        /// <param name="doc"></param>
        /// <param name="parent"></param>
        public void DumpNodeToXml(string[] nodeArray, int lineNum, XmlDocument doc, XmlNode parent)
        {
            if (lineNum >= nodeArray.Length - 1)
                return;

            int grading = SpaceCount(nodeArray[lineNum + 1]) - SpaceCount(nodeArray[lineNum]);
            XmlElement nodeNext = doc.CreateElement("Node");
            SetNodeProperty(nodeNext, nodeArray[lineNum+1]);

            if (grading == 1) // 子级
            {
                parent.AppendChild(nodeNext);
                DumpNodeToXml(nodeArray, lineNum + 1, doc, nodeNext);
            }
            else if (grading == 0) // 同级
            {
                parent.ParentNode.AppendChild(nodeNext);
                DumpNodeToXml(nodeArray, lineNum + 1, doc, nodeNext);
            }
            else if (grading < 0) // 父级
            {
                XmlNode curNode = GetParentNode(grading, parent);
                curNode.AppendChild(nodeNext);
                DumpNodeToXml(nodeArray, lineNum + 1, doc, nodeNext);
            }
        }

        /// <summary>
        /// 获取父节点
        /// </summary>
        /// <param name="num"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        public XmlNode GetParentNode(int num, XmlNode node)
        {
            for (int i = num; i <= 0; i++)
            {
                node = node.ParentNode;
            }
            return node;
        }

        /// <summary>
        /// 设置节点属性
        /// </summary>
        /// <param name="node"></param>
        /// <param name="lineInfo"></param>
        public void SetNodeProperty(XmlElement node, string lineInfo)
        {
            List<string> lineList = lineInfo.Split(new string[] { " " }, StringSplitOptions.RemoveEmptyEntries).ToList();
            string[] firstArray = lineList.First().Split('@');
            node.SetAttribute("class", firstArray[0]);

            if (2 == firstArray.Length)
                node.SetAttribute("hashCode", firstArray[1]);

            for (int i = 1; i < lineList.Count; i++)
            {
                string[] propArray = lineList[i].Split('=');
                if (2 == propArray.Length)
                    node.SetAttribute(propArray[0].Trim(new char[] { '[', ']' }).TrimEnd(new char[] { '(', ')'}).Split(':').Last().Split('/').Last(), propArray[1].Split(new char[] { ',' }, 2).Last());
            }
        }

        /// <summary>
        /// 计算每行节点信息前面的空格，用以获取节点的层次结构
        /// </summary>
        /// <param name="strInfo"></param>
        /// <returns></returns>
        public int SpaceCount(string strInfo)
        {
            for (int i = 0; i < strInfo.Length; i++)
            {
                if (' ' != strInfo[i])
                    return i;
            }

            return 0;
        }
    }
}
