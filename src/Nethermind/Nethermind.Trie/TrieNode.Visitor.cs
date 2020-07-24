﻿//  Copyright (c) 2018 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.

using System.Runtime.CompilerServices;
using Nethermind.Core;
using Nethermind.Serialization.Rlp;

[assembly: InternalsVisibleTo("Ethereum.Trie.Test")]
[assembly: InternalsVisibleTo("Nethermind.Blockchain.Test")]
[assembly: InternalsVisibleTo("Nethermind.Trie.Test")]

namespace Nethermind.Trie
{
    public partial class TrieNode
    {
        internal void Accept(ITreeVisitor visitor, PatriciaTree tree, TrieVisitContext trieVisitContext)
        {
            try
            {
                ResolveNode(tree, false);
            }
            catch (TrieException)
            {
                visitor.VisitMissingNode(Keccak, trieVisitContext);
                return;
            }

            switch (NodeType)
            {
                case NodeType.Branch:
                {
                    visitor.VisitBranch(this, trieVisitContext);
                    trieVisitContext.Level++;
                    for (int i = 0; i < 16; i++)
                    {
                        TrieNode child = GetChild(i);
                        if (child != null && visitor.ShouldVisit(child.Keccak))
                        {
                            trieVisitContext.BranchChildIndex = i;
                            child.Accept(visitor, tree, trieVisitContext);
                        }
                    }

                    trieVisitContext.Level--;
                    trieVisitContext.BranchChildIndex = null;
                    break;
                }

                case NodeType.Extension:
                {
                    visitor.VisitExtension(this, trieVisitContext);
                    TrieNode child = GetChild(0);
                    if (child != null && visitor.ShouldVisit(child.Keccak))
                    {
                        trieVisitContext.Level++;
                        trieVisitContext.BranchChildIndex = null;
                        child.Accept(visitor, tree, trieVisitContext);
                        trieVisitContext.Level--;
                    }

                    break;
                }

                case NodeType.Leaf:
                {
                    visitor.VisitLeaf(this, trieVisitContext, Value);
                    if (!trieVisitContext.IsStorage && trieVisitContext.ExpectAccounts) // can combine these conditions
                    {
                        Account account = _accountDecoder.Decode(Value.AsRlpStream());
                        if (account.HasCode && visitor.ShouldVisit(account.CodeHash))
                        {
                            trieVisitContext.Level++;
                            trieVisitContext.BranchChildIndex = null;
                            visitor.VisitCode(account.CodeHash, trieVisitContext);
                            trieVisitContext.Level--;
                        }

                        if (account.HasStorage && visitor.ShouldVisit(account.StorageRoot))
                        {
                            trieVisitContext.IsStorage = true;
                            TrieNode storageRoot = new TrieNode(NodeType.Unknown, account.StorageRoot);
                            trieVisitContext.Level++;
                            trieVisitContext.BranchChildIndex = null;
                            storageRoot.Accept(visitor, tree, trieVisitContext);
                            trieVisitContext.Level--;
                            trieVisitContext.IsStorage = false;
                        }
                    }

                    break;
                }

                default:
                    throw new TrieException($"An attempt was made to visit a node {Keccak} of type {NodeType}");
            }
        }
    }
}