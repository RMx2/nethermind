/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Logging;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.Evm.Tracing;
using Nethermind.Store;
using NSubstitute;
using NUnit.Framework;

namespace Nethermind.Evm.Test
{
    [TestFixture]
    [Todo(Improve.Refactor, "Check why fixture test cases did not work")]
    public class TransactionProcessorTests
    {
        private ISpecProvider _specProvider;
        private IEthereumSigner _ethereumSigner;
        private TransactionProcessor _transactionProcessor;
        private StateProvider _stateProvider;
        
        public TransactionProcessorTests()
        {
        }
        
        [SetUp]
        public void Setup()
        {
            _specProvider = MainNetSpecProvider.Instance;
            StateDb stateDb = new StateDb();
            _stateProvider = new StateProvider(new StateTree(stateDb), new MemDb(), NullLogManager.Instance);
            StorageProvider storageProvider = new StorageProvider(stateDb, _stateProvider, NullLogManager.Instance);
            VirtualMachine virtualMachine = new VirtualMachine(_stateProvider, storageProvider, Substitute.For<IBlockhashProvider>(), NullLogManager.Instance);
            _transactionProcessor = new TransactionProcessor(_specProvider, _stateProvider, storageProvider, virtualMachine, NullLogManager.Instance);
            _ethereumSigner = new EthereumSigner(_specProvider, NullLogManager.Instance);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Can_process_simple_transaction(bool withStateDiff, bool withTrace)
        {
            GiveEtherToA();
            Transaction tx = Build.A.Transaction.SignedAndResolved(_ethereumSigner, TestObject.PrivateKeyA, 1).TestObject;

            Block block = Build.A.Block.WithNumber(1).WithTransactions(tx).TestObject;

            BlockReceiptsTracer tracer = BuildTracer(block, tx, withTrace, withTrace);
            Execute(tracer, tx, block);

            Assert.AreEqual(StatusCode.Success, tracer.Receipts[0].StatusCode);
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Can_handle_quick_fail_on_intrinsic_gas(bool withStateDiff, bool withTrace)
        {
            GiveEtherToA();
            Transaction tx = Build.A.Transaction.SignedAndResolved(_ethereumSigner, TestObject.PrivateKeyA, 1).WithGasLimit(20000).TestObject;

            Block block = Build.A.Block.WithNumber(1).WithTransactions(tx).TestObject;

            BlockReceiptsTracer tracer = BuildTracer(block, tx, withTrace, withTrace);
            Execute(tracer, tx, block);

            Assert.AreEqual(StatusCode.Failure, tracer.Receipts[0].StatusCode);
        }
        
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Can_handle_quick_fail_on_missing_sender(bool withStateDiff, bool withTrace)
        {
            GiveEtherToA();
            Transaction tx = Build.A.Transaction.Signed(_ethereumSigner, TestObject.PrivateKeyA, 1).TestObject;

            Block block = Build.A.Block.WithNumber(1).WithTransactions(tx).TestObject;

            BlockReceiptsTracer tracer = BuildTracer(block, tx, withTrace, withTrace);
            Execute(tracer, tx, block);

            Assert.AreEqual(StatusCode.Failure, tracer.Receipts[0].StatusCode);
        }
        
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Can_handle_quick_fail_on_not_enough_balance(bool withStateDiff, bool withTrace)
        {
            Transaction tx = Build.A.Transaction.SignedAndResolved(_ethereumSigner, TestObject.PrivateKeyA, 1).TestObject;

            Block block = Build.A.Block.WithNumber(1).WithTransactions(tx).TestObject;

            BlockReceiptsTracer tracer = BuildTracer(block, tx, withTrace, withTrace);
            Execute(tracer, tx, block);

            Assert.AreEqual(StatusCode.Failure, tracer.Receipts[0].StatusCode);
        }
        
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void Can_handle_quick_fail_on_above_block_gas_limit(bool withStateDiff, bool withTrace)
        {
            Transaction tx = Build.A.Transaction.SignedAndResolved(_ethereumSigner, TestObject.PrivateKeyA, 1).TestObject;

            Block block = Build.A.Block.WithNumber(1).WithTransactions(tx).WithGasLimit(20000).TestObject;

            BlockReceiptsTracer tracer = BuildTracer(block, tx, withTrace, withTrace);
            Execute(tracer, tx, block);

            Assert.AreEqual(StatusCode.Failure, tracer.Receipts[0].StatusCode);
        }
        
        private BlockReceiptsTracer BuildTracer(Block block, Transaction tx, bool stateDiff, bool trace)
        {
            ParityTraceTypes types = ParityTraceTypes.None;
            if (stateDiff)
            {
                types = types | ParityTraceTypes.StateDiff;
            }
            
            if (trace)
            {
                types = types | ParityTraceTypes.Trace;
            }
            
            IBlockTracer otherTracer = types != ParityTraceTypes.None ? new ParityLikeBlockTracer(block, tx.Hash, ParityTraceTypes.Trace | ParityTraceTypes.StateDiff) : (IBlockTracer)NullBlockTracer.Instance; 
            BlockReceiptsTracer tracer = new BlockReceiptsTracer(block, otherTracer, _specProvider, _stateProvider);
            return tracer;
        }

        private void GiveEtherToA()
        {
            _stateProvider.CreateAccount(TestObject.PrivateKeyA.Address, 1.Ether());
            _stateProvider.Commit(_specProvider.GetSpec(1));
        }

        private void Execute(BlockReceiptsTracer tracer, Transaction tx, Block block)
        {
            tracer.StartNewTxTrace(tx.Hash);
            _transactionProcessor.Execute(tx, block.Header, tracer);
            tracer.EndTxTrace();
        }
    }
}