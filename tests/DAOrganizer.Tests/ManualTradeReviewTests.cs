using DAOrganizer.App;
using DAOrganizer.Core;
using Xunit;

namespace DAOrganizer.Tests;

public class ManualTradeReviewTests
{
    [Fact]
    public void SavingAcceptedCaptureWithoutProvenCloseRecordsNoSuccess()
    {
        var directory=Path.Combine(Path.GetTempPath(),"daorganizer-trade-review-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var organizer=new Organizer(directory);
            var (sender,recipient)=ManualTradeAnalyzerTests.PartialStack();
            var path=organizer.SaveManualTradeCapture(sender,recipient);
            Assert.False(organizer.LastManualTradeAnalysis?.Verified);
            Assert.True(File.Exists(Path.Combine(path,"first.json")));
            Assert.True(File.Exists(Path.Combine(path,"second.json")));
            Assert.Equal(Tradeability.Unknown,organizer.Store.TradeEvidence(sender.BeforeInventory[0]).State);
            organizer.SaveManualTradeCapture(sender,recipient);
            Assert.Equal(0,organizer.Store.TradeEvidence(sender.BeforeInventory[0]).Successes);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }

    [Fact]
    public void SavingAmbiguousCaptureDoesNotRecordTradeability()
    {
        var directory=Path.Combine(Path.GetTempPath(),"daorganizer-trade-review-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var organizer=new Organizer(directory);
            var (sender,recipient)=ManualTradeAnalyzerTests.PartialStack();
            recipient=recipient with {AfterInventory=[]};
            organizer.SaveManualTradeCapture(sender,recipient);
            Assert.False(organizer.LastManualTradeAnalysis?.Verified);
            Assert.Equal(Tradeability.Unknown,organizer.Store.TradeEvidence(sender.BeforeInventory[0]).State);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
}
